using System.Globalization;
using Agent.Core;
using Agent.Core.Confirm;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Text;
using Agent.Core.Trace;
using Agent.Core.Wallet;
using Agent.Web.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Agent.Web.Services;

/// <summary>
/// The phone's conversation for one circuit (scoped): the messages, the card, the open question, and the actions the
/// components call. Every action opens its own DI scope (<see cref="IServiceScopeFactory.CreateAsyncScope"/>) and
/// resolves the agent's services from it, so no two UI events share a database context; a <see cref="SemaphoreSlim"/>
/// runs one action at a time, so a timer refresh and a tap never overlap. Every text it shows comes from the resource
/// file (rule 4), every card field from <see cref="CardView"/> (rule 3), and words never execute: only
/// <see cref="ConfirmAsync"/> (the card's Confirm button) and <see cref="SettleAsync"/> (the "check again" button) reach
/// the Confirm handler (rule 1, <c>NoTapNoMoneyTests</c>).
/// </summary>
public sealed class ChatSession : IDisposable
{
    private readonly IServiceScopeFactory scopes;
    private readonly IParser parser;
    private readonly TimeProvider clock;
    private readonly IStringLocalizer<PhoneStrings> text;
    private readonly TraceLog trace;
    private readonly int userId;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken stopping;
    private readonly List<ChatMessage> messages = [];

    private ParsedRequest? lastRequest;
    private string? typedSlot;
    private int nextId;
    private bool disposed;

    public ChatSession(
        IServiceScopeFactory scopes,
        IParser parser,
        TimeProvider clock,
        IStringLocalizer<PhoneStrings> text,
        TraceLog trace,
        IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(options);

        this.scopes = scopes;
        this.parser = parser;
        this.clock = clock;
        this.text = text;
        this.trace = trace;
        userId = options.Value.DemoUserId;
        stopping = lifetime.Token;
    }

    /// <summary>Raised after every change of state; handlers must not block (components call <c>InvokeAsync</c>).</summary>
    public event Action? Changed;

    public IReadOnlyList<ChatMessage> Messages => messages;

    /// <summary>The card on the phone, projected by code from its pending record; the record never reaches a component.</summary>
    public CardView? Card { get; private set; }

    public CardStatus? CardState { get; private set; }

    /// <summary>The message the card is shown under.</summary>
    public int? CardMessageId { get; private set; }

    /// <summary>The open question; a tap is accepted only for one of its options.</summary>
    public AgentReply.Question? Question { get; private set; }

    /// <summary>The message the question's options are shown under.</summary>
    public int? QuestionMessageId { get; private set; }

    /// <summary>The message whose button (consent, or check again) is live; older ones lose theirs.</summary>
    public int? ActionMessageId { get; private set; }

    /// <summary>True while the card needs a PIN field.</summary>
    public bool PinVisible { get; private set; }

    /// <summary>True while an action runs; the buttons are disabled.</summary>
    public bool Busy { get; private set; }

    /// <summary>Goes up after every Done outcome, so the wallet data panel knows to reload.</summary>
    public int WalletVersion { get; private set; }

    /// <summary>The time the card's countdown measures against.</summary>
    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>
    /// A typed message. With a live card, the yes word points to the Confirm button (D6: words never confirm), the no
    /// word cancels the card, and anything else cancels it and goes on as a new request. A message that answers a
    /// typed question (<c>which_amount</c> or <c>which_contact</c> with no options) fills that slot, unless it is a
    /// scripted sentence of its own.
    /// </summary>
    public async Task SendAsync(string typedText)
    {
        ArgumentNullException.ThrowIfNull(typedText);
        TraceLog.Current.Value = trace;
        if (!await EnterAsync())
            return;
        try
        {
            SetBusy(true);
            var token = stopping;
            trace.Record(TraceZone.User, "message_received", FormattableString.Invariant($"length {typedText.Length}"));
            Add(ChatRole.User, typedText, ChatMessageKind.Text);

            var pendingNote = PendingNote();
            var normalized = ArabicText.Normalize(typedText);
            var isYes = IsWord(normalized, "Word_yes");
            var isNo = IsWord(normalized, "Word_no");

            if (IsCardLive)
            {
                if (isYes)
                {
                    // D6: a typed yes never confirms; it points to the button.
                    trace.Record(TraceZone.Code, "points_to_confirm", null);
                    Add(ChatRole.Agent, Text("Refusal_" + ReasonCodes.PointsToConfirm), ChatMessageKind.Text);
                    return;
                }

                await CancelCardAsync(token);
                if (isNo)
                    return;
            }

            var parsed = await parser.ParseAsync(typedText, new ParseContext(pendingNote), token);
            trace.Record(TraceZone.Model, "parse:scripted", parsed.Intent.ToString());

            var request = parsed;
            if (parsed.Intent == RequestIntent.Unclear && !isYes && !isNo && typedSlot is not null && lastRequest is not null)
                request = FillTypedSlot(lastRequest, typedSlot, typedText) ?? parsed;

            await HandleCoreAsync(request, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The circuit is gone; the agent has recorded what it could (see Dispose).
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// A tap on an option of the open question. A value that was not offered is ignored (traced as
    /// <c>option_rejected</c>). Until the orchestrator has an answer entry point, the original request is sent again
    /// with the slot filled from the option.
    /// </summary>
    public async Task TapOptionAsync(string value)
    {
        TraceLog.Current.Value = trace;
        if (!await EnterAsync())
            return;
        try
        {
            SetBusy(true);
            var token = stopping;
            var question = Question;
            var option = question?.Options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.Ordinal));
            if (question is null || option is null || lastRequest is null)
            {
                trace.Record(TraceZone.Code, "option_rejected", null);
                return;
            }

            Add(ChatRole.User, OptionText(option), ChatMessageKind.Text);
            var request = await RequestForOptionAsync(question.Kind, option, lastRequest, token);
            if (request is null)
            {
                Add(ChatRole.Agent, Text("Refusal_" + ReasonCodes.NotDoneNothingTaken), ChatMessageKind.Text);
                return;
            }

            await HandleCoreAsync(request, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The circuit is gone; the agent has recorded what it could (see Dispose).
        }
        finally
        {
            Release();
        }
    }

    /// <summary>The consent button: records consent in the wallet, then answers the last request again.</summary>
    public async Task GiveConsentAsync()
    {
        TraceLog.Current.Value = trace;
        if (!await EnterAsync())
            return;
        try
        {
            SetBusy(true);
            var token = stopping;
            WalletResult<bool> given;
            await using (var scope = scopes.CreateAsyncScope())
                given = await scope.ServiceProvider.GetRequiredService<WalletClient>().GiveConsentAsync(userId, token);

            if (given is not WalletResult<bool>.Ok)
            {
                Add(ChatRole.Agent, Text("Refusal_" + ReasonCodes.NotDoneNothingTaken), ChatMessageKind.Text);
                return;
            }

            trace.Record(TraceZone.Code, "consent_given", null);
            ActionMessageId = null;
            if (lastRequest is not null)
                await HandleCoreAsync(lastRequest, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The circuit is gone; the agent has recorded what it could (see Dispose).
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// The card's Confirm button (rule 1): the only path from the screen to <see cref="ConfirmHandler.ConfirmAsync"/>,
    /// with the PIN typed on the card. The PIN is passed through and kept nowhere. Returns the outcome, or null when no
    /// card is shown or the outcome is unknown after an exception.
    /// </summary>
    public async Task<ConfirmOutcome?> ConfirmAsync(string? pin)
    {
        TraceLog.Current.Value = trace;
        if (!await EnterAsync())
            return null;
        try
        {
            if (Card is not { } card)
                return null;

            SetBusy(true);
            trace.Record(TraceZone.User, "confirm_tapped", null);
            ConfirmOutcome outcome;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ConfirmHandler>();
                outcome = await handler.ConfirmAsync(userId, card.PendingId, string.IsNullOrEmpty(pin) ? null : pin, stopping);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ShowAfterExceptionAsync(card.PendingId, ex);
                return null;
            }

            ShowOutcome(outcome);
            return outcome;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The circuit is gone mid-call: the handler has taken its cancellation path (back to Pending or Checking).
            return null;
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// The "check again" button of a Checking outcome: the only other path to the Confirm handler, which resends the
    /// same body under the same key (rule 6).
    /// </summary>
    public async Task<ConfirmOutcome?> SettleAsync()
    {
        TraceLog.Current.Value = trace;
        if (!await EnterAsync())
            return null;
        try
        {
            if (Card is not { } card || CardState != CardStatus.Checking)
                return null;

            SetBusy(true);
            trace.Record(TraceZone.User, "check_again_tapped", null);
            ConfirmOutcome outcome;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ConfirmHandler>();
                outcome = await handler.SettleCheckingAsync(userId, card.PendingId, stopping);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ShowAfterExceptionAsync(card.PendingId, ex);
                return null;
            }

            ShowOutcome(outcome);
            return outcome;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The circuit is gone mid-call: the handler has taken its cancellation path (back to Pending or Checking).
            return null;
        }
        finally
        {
            Release();
        }
    }

    /// <summary>The card's Cancel button.</summary>
    public async Task CancelAsync()
    {
        TraceLog.Current.Value = trace;
        if (!await EnterAsync())
            return;
        try
        {
            SetBusy(true);
            trace.Record(TraceZone.User, "cancel_tapped", null);
            if (IsCardLive)
                await CancelCardAsync(stopping);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The circuit is gone; the agent has recorded what it could (see Dispose).
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// Called by the card's countdown when it reaches zero: reads the record again and marks the card expired when its
    /// timer has ended, so the Confirm button goes.
    /// </summary>
    public async Task RefreshAsync()
    {
        TraceLog.Current.Value = trace;
        if (!await EnterAsync())
            return;
        try
        {
            SetBusy(true);
            if (!IsCardLive || Card is not { } card)
                return;

            PendingRecord? record;
            await using (var scope = scopes.CreateAsyncScope())
                record = await scope.ServiceProvider.GetRequiredService<PendingStore>().FindAsync(card.PendingId, userId, stopping);

            var state = record?.State;
            if (record is null || state == PendingState.Expired || (state == PendingState.Pending && record.ExpiresAt <= clock.GetUtcNow()))
            {
                MarkExpired();
            }
            else if (state == PendingState.Cancelled)
            {
                CardState = CardStatus.Cancelled;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The circuit is gone; the agent has recorded what it could (see Dispose).
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// Ends the circuit's session: cancels the token every running action uses, so the agent takes its cancellation path
    /// (a Confirm cancelled before the send goes back to Pending, during it to Checking). The semaphore is deliberately
    /// not disposed: an action still running releases it in its <c>finally</c>, and a disposed semaphore would throw there.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    /// <summary>Waits for the session's turn; false when the session ended while waiting.</summary>
    private async Task<bool> EnterAsync()
    {
        try
        {
            await gate.WaitAsync(stopping);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private bool IsCardLive => Card is not null && CardState == CardStatus.Live;

    /// <summary>D5: the one-line note of what is pending, from the type and codes only (rule 5).</summary>
    private string? PendingNote()
    {
        if (IsCardLive && Card is { } card)
            return "pending_card " + card.Type;
        if (Question is { } question)
            return "open_question " + question.Kind;
        return null;
    }

    private async Task HandleCoreAsync(ParsedRequest request, CancellationToken token)
    {
        lastRequest = request;
        Question = null;
        QuestionMessageId = null;
        typedSlot = null;

        AgentReply reply;
        await using (var scope = scopes.CreateAsyncScope())
            reply = await scope.ServiceProvider.GetRequiredService<Orchestrator>().HandleAsync(userId, request, token);

        switch (reply)
        {
            case AgentReply.Refusal refusal:
                Add(ChatRole.Agent, RefusalText(refusal.Code, refusal.AmountIqd), ChatMessageKind.Text);
                break;

            case AgentReply.Question question:
                Question = question;
                QuestionMessageId = Add(ChatRole.Agent, Text("Question_" + question.Kind), ChatMessageKind.Question).Id;
                if (question.Answer == QuestionAnswer.Typed)
                    typedSlot = question.Kind;
                break;

            case AgentReply.BalanceReply balance:
                Add(ChatRole.Agent, Text("Reply_balance", PhoneFormat.Amount(balance.BalanceIqd, text)), ChatMessageKind.Text);
                break;

            case AgentReply.ConsentScreen:
                ActionMessageId = Add(ChatRole.Agent, Text("Reply_consent"), ChatMessageKind.Text, action: ChatAction.Consent).Id;
                break;

            case AgentReply.CardIssued issued:
                Card = issued.View;
                CardState = CardStatus.Live;
                PinVisible = Card.PinRequired;
                CardMessageId = Add(ChatRole.Agent, Text("Card_Title"), ChatMessageKind.Card).Id;
                break;

            case AgentReply.Unsupported unsupported:
                var key = "Unsupported_" + unsupported.Reason;
                Add(ChatRole.Agent, text[key].ResourceNotFound ? Text("Refusal_" + ReasonCodes.NotSupported) : Text(key), ChatMessageKind.Text);
                break;

            default:
                throw new InvalidOperationException("Unknown reply.");
        }
    }

    /// <summary>The request again with the typed answer in its slot; null when the text does not fill it.</summary>
    private static ParsedRequest? FillTypedSlot(ParsedRequest last, string slot, string typedText)
    {
        var typed = typedText.Trim();
        if (slot == QuestionKinds.WhichAmount)
        {
            return decimal.TryParse(ArabicDigits.ToAscii(typed), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                ? last with { AmountValue = amount, AmountUnit = AmountUnit.None }
                : null;
        }

        if (slot == QuestionKinds.WhichContact && typed.Length > 0)
            return last with { RecipientText = typed };

        return null;
    }

    /// <summary>The request a tapped option stands for: the original request with the option's slot filled.</summary>
    private async Task<ParsedRequest?> RequestForOptionAsync(string kind, Option option, ParsedRequest last, CancellationToken token)
    {
        switch (kind)
        {
            case QuestionKinds.WhichContact:
            case QuestionKinds.DidYouMean:
                // The display text is the saved name or alias that matched, so it resolves exactly.
                return last with { RecipientText = option.Display };

            case QuestionKinds.WhichAmount:
                return last with { AmountValue = option.AmountIqd, AmountUnit = AmountUnit.Dinars };

            case QuestionKinds.WhichOperator:
                return last with { OperatorText = option.Value };

            case QuestionKinds.WhichProduct:
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var read = await scope.ServiceProvider.GetRequiredService<WalletClient>().GetProductsAsync(null, null, null, token);
                    if (read is not WalletResult<IReadOnlyList<WalletProduct>>.Ok { Value: var products })
                        return null;
                    var product = products.FirstOrDefault(p => string.Equals(p.Code, option.Value, StringComparison.Ordinal));
                    return product is null ? null : ParsedRequest.Purchase(product.Provider, product.Type, product.FaceValueIqd);
                }

            case QuestionKinds.WhichBill:
            case QuestionKinds.BillOrStore:
                {
                    if (option.Value == Option.StoreInternet)
                        return ParsedRequest.Purchase(null, BillResolver.InternetProductType, null);

                    await using var scope = scopes.CreateAsyncScope();
                    var read = await scope.ServiceProvider.GetRequiredService<WalletClient>().GetBillsAsync(userId, token);
                    if (read is not WalletResult<IReadOnlyList<WalletBill>>.Ok { Value: var bills })
                        return null;
                    var bill = bills.FirstOrDefault(b => string.Equals(b.Code, option.Value, StringComparison.Ordinal));
                    if (bill is null)
                        return null;
                    var named = last.Intent == RequestIntent.Bill ? last.AmountValue : null;
                    return ParsedRequest.Bill(bill.BillerCode, bill.Label, named, named is null ? AmountUnit.None : last.AmountUnit);
                }

            default:
                return null;
        }
    }

    private string OptionText(Option option)
    {
        if (option.Value == Option.StoreInternet)
            return Text("Option_store_internet");
        if (option.Display is { } display)
            return display;
        return option.AmountIqd is { } amount ? PhoneFormat.Amount(amount, text) : option.Value;
    }

    private void ShowOutcome(ConfirmOutcome outcome)
    {
        switch (outcome)
        {
            case ConfirmOutcome.Done done:
                CardState = CardStatus.Done;
                PinVisible = false;
                ActionMessageId = null;
                Add(
                    ChatRole.Agent,
                    Text("Outcome_done", PhoneFormat.Digits(done.Reference), PhoneFormat.Amount(done.BalanceAfterIqd, text)),
                    ChatMessageKind.Outcome,
                    "done");
                if (done.PurchaseCode is { } code)
                    Add(ChatRole.Agent, Text("Purchase_code", PhoneFormat.Digits(code)), ChatMessageKind.Outcome, "done");
                WalletVersion++;
                break;

            case ConfirmOutcome.NotDone notDone:
                CardState = CardStatus.NotDone;
                PinVisible = false;
                ActionMessageId = null;
                Add(ChatRole.Agent, Text("Outcome_not_done", RefusalText(notDone.Reason, notDone.AmountIqd)), ChatMessageKind.Outcome, "not_done");
                break;

            case ConfirmOutcome.Checking:
                CardState = CardStatus.Checking;
                PinVisible = false;
                ActionMessageId = Add(ChatRole.Agent, Text("Outcome_checking"), ChatMessageKind.Outcome, "checking", ChatAction.CheckAgain).Id;
                break;

            case ConfirmOutcome.Expired:
                MarkExpired();
                break;

            case ConfirmOutcome.AlreadyHandled handled:
                if (CardState == CardStatus.Live)
                    CardState = StatusOf(handled.State) ?? CardState;
                Add(ChatRole.Agent, Text("Outcome_already_handled"), ChatMessageKind.Outcome, "already_handled");
                break;

            case ConfirmOutcome.NotFound:
                Add(ChatRole.Agent, Text("Outcome_already_handled"), ChatMessageKind.Outcome, "already_handled");
                break;

            case ConfirmOutcome.PinRequired:
                PinVisible = true;
                Add(ChatRole.Agent, Text("Outcome_pin_required"), ChatMessageKind.Outcome, "pin");
                break;

            case ConfirmOutcome.PinWrong wrong:
                PinVisible = true;
                Add(ChatRole.Agent, Text("Outcome_pin_wrong", PhoneFormat.Number(wrong.AttemptsLeft)), ChatMessageKind.Outcome, "pin");
                break;

            case ConfirmOutcome.PinMalformed:
                PinVisible = true;
                Add(ChatRole.Agent, Text("Outcome_pin_malformed"), ChatMessageKind.Outcome, "pin");
                break;

            default:
                throw new InvalidOperationException("Unknown confirm outcome.");
        }
    }

    /// <summary>
    /// After an exception from the Confirm handler: the handler has put the record back to Pending (nothing sent) or to
    /// Checking (maybe sent). Read where it is; an unknown state is shown as checking, never as failed (rule 6).
    /// </summary>
    private async Task ShowAfterExceptionAsync(Guid pendingId, Exception ex)
    {
        trace.Record(TraceZone.Code, "error", ex.GetType().Name);
        PendingRecord? record = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            record = await scope.ServiceProvider.GetRequiredService<PendingStore>().FindAsync(pendingId, userId, stopping);
        }
        catch (Exception readFailure) when (readFailure is not OperationCanceledException)
        {
            trace.Record(TraceZone.Code, "error", readFailure.GetType().Name);
        }

        if (record?.State == PendingState.Pending)
        {
            Add(ChatRole.Agent, Text("Refusal_" + ReasonCodes.NotDoneNothingTaken), ChatMessageKind.Text);
            return;
        }

        ShowOutcome(new ConfirmOutcome.Checking());
    }

    private async Task CancelCardAsync(CancellationToken token)
    {
        if (Card is not { } card)
            return;

        bool cancelled;
        await using (var scope = scopes.CreateAsyncScope())
            cancelled = await scope.ServiceProvider.GetRequiredService<PendingStore>().CancelAsync(card.PendingId, userId, token);

        if (cancelled)
        {
            CardState = CardStatus.Cancelled;
            PinVisible = false;
            Add(ChatRole.Agent, Text("Refusal_" + ReasonCodes.CardCancelled), ChatMessageKind.Outcome, "cancelled");
            return;
        }

        // The record left Pending meanwhile (its timer ended): show that instead.
        MarkExpired();
    }

    private void MarkExpired()
    {
        if (CardState == CardStatus.Expired)
        {
            Add(ChatRole.Agent, Text("Card_Expired"), ChatMessageKind.Outcome, "expired");
            return;
        }

        CardState = CardStatus.Expired;
        PinVisible = false;
        Add(ChatRole.Agent, Text("Card_Expired"), ChatMessageKind.Outcome, "expired");
    }

    private static CardStatus? StatusOf(PendingState state) => state switch
    {
        PendingState.Done => CardStatus.Done,
        PendingState.NotDone => CardStatus.NotDone,
        PendingState.Checking => CardStatus.Checking,
        PendingState.Expired => CardStatus.Expired,
        PendingState.Cancelled => CardStatus.Cancelled,
        _ => null,
    };

    /// <summary>A reason code's text, with its figure (the minimum, a cap, what is left, the shortfall) when it has one.</summary>
    private string RefusalText(string code, long? amountIqd)
    {
        var key = "Refusal_" + code;
        if (text[key].ResourceNotFound)
            key = "Refusal_" + ReasonCodes.NotDoneNothingTaken;
        return amountIqd is { } amount ? Text(key, PhoneFormat.Amount(amount, text)) : Text(key);
    }

    private bool IsWord(string normalized, string key)
        => normalized.Length > 0 && string.Equals(normalized, ArabicText.Normalize(text[key].Value), StringComparison.Ordinal);

    private string Text(string key) => text[key].Value;

    private string Text(string key, params object[] arguments) => text[key, arguments].Value;

    private ChatMessage Add(ChatRole role, string body, ChatMessageKind kind, string? outcome = null, ChatAction action = ChatAction.None)
    {
        var message = new ChatMessage(++nextId, role, body, kind, outcome, action);
        messages.Add(message);
        return message;
    }

    /// <summary>An action that failed for a reason other than the wallet's answer: no card, nothing taken.</summary>
    private void Failed(Exception ex)
    {
        trace.Record(TraceZone.Code, "error", ex.GetType().Name);
        Add(ChatRole.Agent, Text("Refusal_" + ReasonCodes.NotDoneNothingTaken), ChatMessageKind.Text);
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        Changed?.Invoke();
    }

    private void Release()
    {
        Busy = false;
        gate.Release();
        Changed?.Invoke();
    }
}
