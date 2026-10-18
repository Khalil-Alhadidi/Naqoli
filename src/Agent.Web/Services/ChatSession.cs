using Agent.Core;
using Agent.Core.Aliases;
using Agent.Core.Confirm;
using Agent.Core.Conversation;
using Agent.Core.Parsing;
using Agent.Core.Pipeline;
using Agent.Core.Wallet;
using Agent.Web.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Agent.Web.Services;

/// <summary>
/// The phone's adapter over <see cref="Conversation"/> for one circuit (scoped). The conversation holds the state machine
/// and the actions, and speaks in codes and figures; this adapter turns each of its messages into the localised
/// <see cref="ChatMessage"/> the components render (rule 4: every text from the resource file), keeps the user's own
/// words for the screen, routes the agent's trace to the circuit's <see cref="TraceLog"/>, and raises
/// <see cref="Changed"/>. Only <see cref="ConfirmAsync"/> (the card's Confirm button) and <see cref="SettleAsync"/> (the
/// "check again" button) reach the conversation's Confirm and settle (rule 1, <c>NoTapNoMoneyTests</c>).
/// </summary>
public sealed class ChatSession : IDisposable
{
    /// <summary>
    /// The words of the message being sent on this async flow. The conversation adds the user's message, and raises
    /// <see cref="Conversation.Changed"/>, on the flow of the action that sent it, so the adapter reads the words here
    /// and the conversation never holds them.
    /// </summary>
    private static readonly AsyncLocal<string?> SendingText = new();

    /// <summary>
    /// The alias text being saved on this async flow (the offer's text, read before the save closes the offer), so the
    /// <see cref="ConversationKind.AliasSaved"/> message can say what was saved.
    /// </summary>
    private static readonly AsyncLocal<string?> SavingAliasText = new();

    private readonly IServiceScopeFactory scopes;
    private readonly IParser parser;
    private readonly TimeProvider clock;
    private readonly IOptionsMonitor<AgentOptions> options;
    private readonly IStringLocalizer<PhoneStrings> text;
    private readonly TraceLog trace;
    private readonly WalletChanges walletChanges;
    private readonly Lock gate = new();
    private readonly List<ChatMessage> messages = [];

    /// <summary>The id of the first chat message made from each conversation message, by its position.</summary>
    private readonly List<int> idOfConversationMessage = [];

    /// <summary>The circuit's conversation; replaced by <see cref="RestartAsync"/>.</summary>
    private Conversation conversation;

    private int nextId;
    private bool disposed;

    /// <summary>The demo user of the current conversation, read from the options when it was built.</summary>
    private int conversationUserId;

    /// <summary>
    /// The sum, over the conversations this session replaced, of each one's <see cref="Conversation.WalletVersion"/> plus
    /// one, so <see cref="WalletVersion"/> never goes back after <see cref="RestartAsync"/> and a restart is itself a new
    /// version (the phone's directory reloads the seed's list after the console's Reset).
    /// </summary>
    private int versionBase;

    /// <summary>The current conversation's <see cref="Conversation.WalletVersion"/> already announced on <see cref="WalletChanges"/>.</summary>
    private int announcedVersion;

    public ChatSession(
        IServiceScopeFactory scopes,
        IParser parser,
        TimeProvider clock,
        IStringLocalizer<PhoneStrings> text,
        TraceLog trace,
        IOptionsMonitor<AgentOptions> options,
        WalletChanges walletChanges)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(walletChanges);

        this.scopes = scopes;
        this.parser = parser;
        this.clock = clock;
        this.options = options;
        this.text = text;
        this.trace = trace;
        this.walletChanges = walletChanges;
        conversation = NewConversation();
    }

    /// <summary>Raised after every change of state; handlers must not block (components call <c>InvokeAsync</c>).</summary>
    public event Action? Changed;

    /// <summary>The messages so far, localised, oldest first (a snapshot).</summary>
    public IReadOnlyList<ChatMessage> Messages
    {
        get
        {
            lock (gate)
                return [.. messages];
        }
    }

    /// <summary>The card on the phone, projected by code from its pending record; the record never reaches a component.</summary>
    public CardView? Card => conversation.Card;

    public CardStatus? CardState => conversation.CardState;

    /// <summary>The message the card is shown under.</summary>
    public int? CardMessageId => IdOf(conversation.CardMessageIndex);

    /// <summary>The open question; a tap is accepted only for one of its options.</summary>
    public AgentReply.Question? Question => conversation.Question;

    /// <summary>The message the question's options are shown under.</summary>
    public int? QuestionMessageId => IdOf(conversation.QuestionMessageIndex);

    /// <summary>The message whose button (consent, or check again) is live; older ones lose theirs.</summary>
    public int? ActionMessageId => IdOf(conversation.ActionMessageIndex);

    /// <summary>True while the card needs a PIN field.</summary>
    public bool PinVisible => conversation.PinVisible;

    /// <summary>True while an action runs; the buttons are disabled.</summary>
    public bool Busy => conversation.Busy;

    /// <summary>
    /// Goes up after every Done outcome, every alias saved or deleted, and every <see cref="RestartAsync"/>, so the phone's
    /// directory knows to reload. Monotonic for the session's life: a restart adds the replaced conversation's version plus
    /// one to the base, so a view holding an older number never matches a newer change. The console panels follow
    /// <see cref="WalletChanges"/> instead, which also carries other circuits' changes.
    /// </summary>
    public int WalletVersion
    {
        get
        {
            lock (gate)
                return versionBase + conversation.WalletVersion;
        }
    }

    /// <summary>The time the card's countdown measures against.</summary>
    public DateTimeOffset Now => conversation.Now;

    /// <summary>The open alias offer (rule 18), or null; its text is the user's own words, never model text.</summary>
    public AliasOffer? AliasOffer => conversation.AliasOffer;

    /// <summary>The message the offer's Save button is shown under.</summary>
    public int? AliasOfferMessageId => IdOf(conversation.AliasOfferMessageIndex);

    /// <summary>The Save button of the alias offer (see <see cref="Conversation.SaveAliasAsync"/>); never reaches the Confirm handler.</summary>
    public async Task<AliasResult?> SaveAliasAsync()
    {
        TraceLog.Current.Value = trace;
        SavingAliasText.Value = conversation.AliasOffer?.Text;
        return await conversation.SaveAliasAsync();
    }

    /// <summary>The alias directory: the user's contacts and aliases (see <see cref="Conversation.ListAliasesAsync"/>).</summary>
    public async Task<WalletResult<WalletContacts>> ListAliasesAsync()
    {
        TraceLog.Current.Value = trace;
        Conversation current;
        lock (gate)
            current = conversation;

        var result = await current.ListAliasesAsync();

        // A read cut short by RestartAsync (the console's Reset raises its wallet change before the restart, so a panel's
        // reload can start on the conversation being replaced) is read again from the new conversation, once.
        lock (gate)
        {
            if (disposed || ReferenceEquals(current, conversation) || result is not WalletResult<WalletContacts>.Unavailable)
                return result;
            current = conversation;
        }

        return await current.ListAliasesAsync();
    }

    /// <summary>The alias directory: an alias of a contact's account (see <see cref="Conversation.AddAliasAsync"/>).</summary>
    public async Task<AliasResult?> AddAliasAsync(string text, int targetAccountId)
    {
        TraceLog.Current.Value = trace;
        return await conversation.AddAliasAsync(text, targetAccountId);
    }

    /// <summary>The alias directory: an alias of the account behind a typed number (see <see cref="Conversation.AddAliasForPhoneAsync"/>).</summary>
    public async Task<AliasResult?> AddAliasForPhoneAsync(string text, string phone)
    {
        TraceLog.Current.Value = trace;
        return await conversation.AddAliasForPhoneAsync(text, phone);
    }

    /// <summary>The alias directory: deletes one alias (see <see cref="Conversation.DeleteAliasAsync"/>).</summary>
    public async Task<AliasResult?> DeleteAliasAsync(int aliasId)
    {
        TraceLog.Current.Value = trace;
        return await conversation.DeleteAliasAsync(aliasId);
    }

    /// <summary>A typed message (see <see cref="Conversation.SendAsync"/>); the words are shown as typed.</summary>
    public async Task SendAsync(string typedText)
    {
        ArgumentNullException.ThrowIfNull(typedText);
        TraceLog.Current.Value = trace;
        SendingText.Value = typedText;
        await conversation.SendAsync(typedText);
    }

    /// <summary>A tap on an option of the open question (see <see cref="Conversation.TapOptionAsync"/>).</summary>
    public async Task TapOptionAsync(string value)
    {
        TraceLog.Current.Value = trace;
        await conversation.TapOptionAsync(value);
    }

    /// <summary>
    /// The question panel's final option (see <see cref="Conversation.DeclineQuestionAsync"/>): "none of these" on a
    /// contact question with options, "cancel" on every other question.
    /// </summary>
    public async Task DeclineQuestionAsync()
    {
        TraceLog.Current.Value = trace;
        await conversation.DeclineQuestionAsync();
    }

    /// <summary>
    /// The open check (see <see cref="Conversation.OpenAsync"/>): the consent screen before any typing for a user who has
    /// not consented. The phone calls it once its circuit is interactive; a second call does nothing.
    /// </summary>
    public async Task OpenAsync()
    {
        TraceLog.Current.Value = trace;
        Conversation current;
        lock (gate)
        {
            if (disposed)
                return;
            current = conversation;
        }

        await current.OpenAsync();
    }

    /// <summary>The consent button.</summary>
    public async Task GiveConsentAsync()
    {
        TraceLog.Current.Value = trace;
        await conversation.GiveConsentAsync();
    }

    /// <summary>The card's Confirm button (rule 1), with the PIN typed on the card; the PIN is passed through and kept nowhere.</summary>
    public async Task<ConfirmOutcome?> ConfirmAsync(string? pin)
    {
        TraceLog.Current.Value = trace;
        return await conversation.ConfirmAsync(pin);
    }

    /// <summary>The "check again" button of a Checking outcome: the same body under the same key (rule 6).</summary>
    public async Task<ConfirmOutcome?> SettleAsync()
    {
        TraceLog.Current.Value = trace;
        return await conversation.SettleAsync();
    }

    /// <summary>
    /// What the card shown offers (D19): <see cref="ReissueKind.TryAgain"/>, <see cref="ReissueKind.SendAgain"/>, or
    /// null for a live card, a payment still checking, or no card.
    /// </summary>
    public ReissueKind? Reissue => Conversation.ReissueKindOf(conversation.CardState);

    /// <summary>
    /// The card's "Try again" or "Send again" button (see <see cref="Conversation.ReissueAsync"/>): a new card from the
    /// stored record, which waits for its own Confirm. It never reaches the Confirm handler.
    /// </summary>
    public async Task ReissueAsync()
    {
        TraceLog.Current.Value = trace;
        await conversation.ReissueAsync();
    }

    /// <summary>The card's Cancel button.</summary>
    public async Task CancelAsync()
    {
        TraceLog.Current.Value = trace;
        await conversation.CancelAsync();
    }

    /// <summary>Called by the card's countdown when it reaches zero.</summary>
    public async Task RefreshAsync()
    {
        TraceLog.Current.Value = trace;
        await conversation.RefreshAsync();
    }

    /// <summary>
    /// Starts the phone over (the console's Reset control, after the wallet's reset): the current conversation is
    /// unsubscribed and disposed, so a running action takes its cancellation path; a new one is built for the same demo
    /// user; the messages and their ids are cleared; <see cref="Changed"/> is raised; and the new conversation's open
    /// check runs (<see cref="OpenAsync"/>), so a user the reset left not consented sees the consent screen at once.
    /// </summary>
    public async Task RestartAsync()
    {
        Conversation previous;
        lock (gate)
        {
            if (disposed)
                return;

            previous = conversation;
            previous.Changed -= OnConversationChanged;
            versionBase += previous.WalletVersion + 1;
            announcedVersion = 0;
            conversation = NewConversation();
            messages.Clear();
            idOfConversationMessage.Clear();
            nextId = 0;
        }

        previous.Dispose();
        Changed?.Invoke();
        await OpenAsync();
    }

    /// <summary>Ends the circuit's session: the conversation's running action takes its cancellation path.</summary>
    public void Dispose()
    {
        Conversation current;
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            current = conversation;
        }

        current.Changed -= OnConversationChanged;
        current.Dispose();
    }

    /// <summary>A conversation for the configured demo user, subscribed to this adapter.</summary>
    private Conversation NewConversation()
    {
        conversationUserId = options.CurrentValue.DemoUserId;
        var created = new Conversation(scopes, parser, clock, options, trace, conversationUserId);
        created.Changed += OnConversationChanged;
        return created;
    }

    private void OnConversationChanged()
    {
        Sync();
        AnnounceWalletChange();
        Changed?.Invoke();
    }

    /// <summary>
    /// Raises one <see cref="WalletChange"/> for the demo user when the conversation's <see cref="Conversation.WalletVersion"/>
    /// went up since the last call. The kind comes from <see cref="Conversation.LastWalletChange"/>, which the conversation
    /// sets beside each rise (a Done outcome is a payment; the offer's Save and the directory's save and delete are
    /// aliases). The conversation runs one action at a time and raises <see cref="Conversation.Changed"/> at least once
    /// after each rise, so every rise is announced once.
    /// </summary>
    private void AnnounceWalletChange()
    {
        WalletChange change;
        lock (gate)
        {
            var version = conversation.WalletVersion;
            if (disposed || version <= announcedVersion)
                return;

            announcedVersion = version;
            var kind = conversation.LastWalletChange == ConversationWalletChange.Payment ? WalletChangeKind.Payment : WalletChangeKind.Alias;
            change = new WalletChange(conversationUserId, kind);
        }

        walletChanges.Raise(change);
    }

    /// <summary>Localises the conversation's messages added since the last call.</summary>
    private void Sync()
    {
        var all = conversation.Messages;
        lock (gate)
        {
            for (var i = idOfConversationMessage.Count; i < all.Count; i++)
            {
                idOfConversationMessage.Add(nextId + 1);
                foreach (var (role, body, kind, outcome, action) in Localise(all[i]))
                    messages.Add(new ChatMessage(++nextId, role, body, kind, outcome, action));
            }
        }
    }

    private int? IdOf(int? index)
    {
        if (index is not { } position)
            return null;
        lock (gate)
            return position < idOfConversationMessage.Count ? idOfConversationMessage[position] : null;
    }

    /// <summary>The chat messages one conversation message becomes (a Done purchase becomes two: the outcome and the code).</summary>
    private IEnumerable<(ChatRole Role, string Body, ChatMessageKind Kind, string? Outcome, ChatAction Action)> Localise(ConversationMessage message)
    {
        switch (message.Kind)
        {
            case ConversationKind.Typed:
                yield return (ChatRole.User, SendingText.Value ?? string.Empty, ChatMessageKind.Text, null, ChatAction.None);
                break;

            case ConversationKind.Option:
                yield return (ChatRole.User, OptionText(message.Code), ChatMessageKind.Text, null, ChatAction.None);
                break;

            case ConversationKind.Refusal when message.QuestionKind is { } repeated:
                // Not understood while a question is open: the question is asked again in the same message.
                yield return (ChatRole.Agent, Text("Refusal_not_understood_repeat", Text("Question_" + repeated)), ChatMessageKind.Text, null, ChatAction.None);
                break;

            case ConversationKind.Refusal:
                yield return (ChatRole.Agent, RefusalText(message.Code, message.AmountIqd), ChatMessageKind.Text, null, ChatAction.None);
                break;

            case ConversationKind.Info:
                yield return (ChatRole.Agent, Text("Refusal_" + message.Code), ChatMessageKind.Text, null, ChatAction.None);
                break;

            case ConversationKind.Unsupported:
                var key = "Unsupported_" + message.Code;
                var body = text[key].ResourceNotFound ? Text("Refusal_" + ReasonCodes.NotSupported) : Text(key);
                yield return (ChatRole.Agent, body, ChatMessageKind.Text, null, ChatAction.None);
                break;

            case ConversationKind.Question:
                yield return (ChatRole.Agent, Text("Question_" + message.Code), ChatMessageKind.Question, null, ChatAction.None);
                break;

            case ConversationKind.Balance:
                yield return (ChatRole.Agent, Text("Reply_balance", Amount(message.AmountIqd)), ChatMessageKind.Text, null, ChatAction.None);
                break;

            case ConversationKind.Consent:
                yield return (ChatRole.Agent, Text("Reply_consent"), ChatMessageKind.Text, null, ChatAction.Consent);
                break;

            case ConversationKind.Card:
                yield return (ChatRole.Agent, Text("Card_Title"), ChatMessageKind.Card, null, ChatAction.None);
                break;

            case ConversationKind.Outcome:
                foreach (var outcome in LocaliseOutcome(message))
                    yield return outcome;
                break;

            case ConversationKind.AliasOffer:
                // Rule 18: the user's own words, as typed, as {0} and the account's name from the wallet as {1} (review
                // R6), in the template; the Save button sits under it.
                yield return (
                    ChatRole.Agent,
                    Text("Offer_save_alias", conversation.AliasOffer?.Text ?? string.Empty, conversation.AliasOffer?.RecipientName ?? string.Empty),
                    ChatMessageKind.Text,
                    null,
                    ChatAction.SaveAlias);
                break;

            case ConversationKind.AliasSaved:
                yield return (ChatRole.Agent, Text("Reply_alias_saved", SavingAliasText.Value ?? string.Empty), ChatMessageKind.Text, null, ChatAction.None);
                break;

            default:
                throw new InvalidOperationException("Unknown conversation message.");
        }
    }

    private IEnumerable<(ChatRole Role, string Body, ChatMessageKind Kind, string? Outcome, ChatAction Action)> LocaliseOutcome(ConversationMessage message)
    {
        switch (message.Code)
        {
            case OutcomeCodes.Done:
                yield return Out(Text("Outcome_done", PhoneFormat.Digits(message.Reference ?? string.Empty), Amount(message.AmountIqd)), "done");
                if (message.PurchaseCode is { } code)
                    yield return Out(Text("Purchase_code", PhoneFormat.Digits(code)), "done");
                break;

            case OutcomeCodes.NotDone:
                yield return Out(Text("Outcome_not_done", NotDoneReasonText(message.Reason, message.AmountIqd)), "not_done");
                break;

            case OutcomeCodes.Checking:
                yield return Out(Text("Outcome_checking"), "checking", ChatAction.CheckAgain);
                break;

            case OutcomeCodes.Expired:
                yield return Out(Text("Card_Expired"), "expired");
                break;

            case OutcomeCodes.Cancelled:
                yield return Out(Text("Refusal_" + ReasonCodes.CardCancelled), "cancelled");
                break;

            case OutcomeCodes.AlreadyHandled:
            case OutcomeCodes.NotFound:
                yield return Out(Text("Outcome_already_handled"), "already_handled");
                break;

            case OutcomeCodes.PinRequired:
                yield return Out(Text("Outcome_pin_required"), "pin");
                break;

            case OutcomeCodes.PinWrong:
                yield return Out(Text("Outcome_pin_wrong", PhoneFormat.Number(message.AttemptsLeft ?? 0)), "pin");
                break;

            case OutcomeCodes.PinMalformed:
                yield return Out(Text("Outcome_pin_malformed"), "pin");
                break;

            default:
                throw new InvalidOperationException("Unknown outcome code.");
        }
    }

    private static (ChatRole Role, string Body, ChatMessageKind Kind, string? Outcome, ChatAction Action) Out(
        string body, string outcome, ChatAction action = ChatAction.None)
        => (ChatRole.Agent, body, ChatMessageKind.Outcome, outcome, action);

    /// <summary>
    /// The text of a tapped option, from the open question (the conversation adds the tap while the question is still
    /// open): the store option's string, the final option's string ("none of these" or "cancel"), the display name, or
    /// the amount.
    /// </summary>
    private string OptionText(string? value)
    {
        var option = conversation.Question?.Options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.Ordinal));
        if (value == Option.StoreInternet)
            return Text("Option_store_internet");
        if (value == Option.NoneOfThese)
            return Text("Option_NoneOfThese");
        if (value == Option.CancelQuestion)
            return Text("Option_CancelQuestion");
        if (option?.Display is { } display)
            return display;
        if (option?.AmountIqd is { } amount)
            return PhoneFormat.Amount(amount, text);
        return value ?? string.Empty;
    }

    /// <summary>A reason code's text, with its figure (the minimum, a cap, what is left, the shortfall) when it has one.</summary>
    private string RefusalText(string? code, long? amountIqd)
    {
        var key = "Refusal_" + code;
        if (code is null || text[key].ResourceNotFound)
            key = "Refusal_" + ReasonCodes.NotDoneNothingTaken;
        return amountIqd is { } amount ? Text(key, PhoneFormat.Amount(amount, text)) : Text(key);
    }

    /// <summary>
    /// The reason nested in <c>Outcome_not_done</c>: the short "nothing taken" text wherever <see cref="RefusalText"/> would
    /// give <c>Refusal_not_done_nothing_taken</c> (that reason, none, or one with no text), since the outcome already says
    /// it did not go through; any other reason as <see cref="RefusalText"/> gives it, with its figure (Khalil, 18 Oct 2026).
    /// </summary>
    private string NotDoneReasonText(string? code, long? amountIqd)
    {
        if (code is null || code == ReasonCodes.NotDoneNothingTaken || text["Refusal_" + code].ResourceNotFound)
            return Text("Refusal_nothing_taken_short");
        return RefusalText(code, amountIqd);
    }

    private string Amount(long? amountIqd) => PhoneFormat.Amount(amountIqd ?? 0, text);

    private string Text(string key) => text[key].Value;

    private string Text(string key, params object[] arguments) => text[key, arguments].Value;
}
