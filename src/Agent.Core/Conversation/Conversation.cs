using System.Globalization;
using Agent.Core.Confirm;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Text;
using Agent.Core.Trace;
using Agent.Core.Wallet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agent.Core.Conversation;

/// <summary>
/// One user's conversation with the agent, without a screen: the messages (as codes and figures), the card, the open
/// question, and the actions a user can take. The phone (<c>ChatSession</c> in <c>Agent.Web</c>) and the test runner
/// (<c>Agent.Eval</c>) both drive it, so the runner measures the same logic the phone runs.
/// </summary>
/// <remarks>
/// Every action opens its own DI scope (<see cref="IServiceScopeFactory.CreateAsyncScope"/>) and resolves the agent's
/// services from it, so no two actions share a database context; a <see cref="SemaphoreSlim"/> runs one action at a time,
/// so a timer refresh and a tap never overlap. It holds no reply text (rule 4): every message is a code with its figures,
/// and every card field comes from <see cref="CardView"/> (rule 3). Words never execute: only <see cref="ConfirmAsync"/>
/// (the card's Confirm button) and <see cref="SettleAsync"/> (the "check again" button) reach the Confirm handler
/// (rule 1, <c>NoTapNoMoneyTests</c>). <see cref="Changed"/> is raised while the action still holds its turn, so a
/// handler sees every message on the flow of the action that added it.
/// </remarks>
public sealed class Conversation : IDisposable
{
    private readonly IServiceScopeFactory scopes;
    private readonly IParser parser;
    private readonly TimeProvider clock;
    private readonly ITraceSink trace;
    private readonly int userId;
    private readonly string[] yesWords;
    private readonly string[] noWords;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken stopping;
    private readonly Lock messagesGate = new();
    private readonly List<ConversationMessage> messages = [];
    private readonly List<CardView> cardsIssued = [];
    private readonly List<ConversationQuestion> questionsAsked = [];

    private ParsedRequest? lastRequest;
    private string? typedSlot;
    private bool disposed;

    /// <param name="scopes">Opens one scope per action.</param>
    /// <param name="parser">The parse call (rule 2).</param>
    /// <param name="clock">The agent's clock; the card's timer is measured against it.</param>
    /// <param name="options">The agent's options; <see cref="AgentOptions.YesWords"/> and <see cref="AgentOptions.NoWords"/> are read once here.</param>
    /// <param name="trace">Where this conversation's own steps go (the services resolved per action write to the scope's sink).</param>
    /// <param name="userId">The wallet user the conversation acts for.</param>
    public Conversation(
        IServiceScopeFactory scopes,
        IParser parser,
        TimeProvider clock,
        IOptions<AgentOptions> options,
        ITraceSink trace,
        int userId)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);

        this.scopes = scopes;
        this.parser = parser;
        this.clock = clock;
        this.trace = trace;
        this.userId = userId;
        yesWords = NormalizeWords(options.Value.YesWords);
        noWords = NormalizeWords(options.Value.NoWords);
        stopping = lifetime.Token;
    }

    /// <summary>
    /// Raised after every change of state, including every new message, while the action that made the change still
    /// holds its turn. Handlers must not block and must not call back into an action.
    /// </summary>
    public event Action? Changed;

    /// <summary>The wallet user this conversation acts for.</summary>
    public int UserId => userId;

    /// <summary>The messages so far, oldest first (a snapshot).</summary>
    public IReadOnlyList<ConversationMessage> Messages
    {
        get
        {
            lock (messagesGate)
                return [.. messages];
        }
    }

    /// <summary>The card shown, projected by code from its pending record; the record never leaves the conversation.</summary>
    public CardView? Card { get; private set; }

    public CardStatus? CardState { get; private set; }

    /// <summary>The position in <see cref="Messages"/> of the message the card is shown under.</summary>
    public int? CardMessageIndex { get; private set; }

    /// <summary>The open question; a tap is accepted only for one of its options.</summary>
    public AgentReply.Question? Question { get; private set; }

    /// <summary>The position in <see cref="Messages"/> of the message the open question's options are shown under.</summary>
    public int? QuestionMessageIndex { get; private set; }

    /// <summary>The position in <see cref="Messages"/> of the message whose button (consent, or check again) is live.</summary>
    public int? ActionMessageIndex { get; private set; }

    /// <summary>True while the card needs a PIN field.</summary>
    public bool PinVisible { get; private set; }

    /// <summary>True while an action runs.</summary>
    public bool Busy { get; private set; }

    /// <summary>Goes up after every Done outcome, so a wallet view knows to reload.</summary>
    public int WalletVersion { get; private set; }

    /// <summary>The time the card's countdown measures against.</summary>
    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>Every card issued in this conversation, in order (for the grader).</summary>
    public IReadOnlyList<CardView> CardsIssued
    {
        get
        {
            lock (messagesGate)
                return [.. cardsIssued];
        }
    }

    /// <summary>Every question asked in this conversation, in order (for the grader).</summary>
    public IReadOnlyList<ConversationQuestion> QuestionsAsked
    {
        get
        {
            lock (messagesGate)
                return [.. questionsAsked];
        }
    }

    /// <summary>The last reply the agent gave to a request (the orchestrator's, or <c>model_unavailable</c>).</summary>
    public AgentReply? LastReply { get; private set; }

    /// <summary>
    /// The last request handed to the orchestrator (as parsed, or with a typed or tapped answer in its slot); with
    /// <see cref="LastReply"/> it gives the learning-log entry (<c>LearningLogRule.EntryFor</c>).
    /// </summary>
    public ParsedRequest? LastRequest => lastRequest;

    /// <summary>True once the consent screen has been shown in this conversation.</summary>
    public bool ConsentShown { get; private set; }

    /// <summary>
    /// A typed message. With a live card, a yes word points to the Confirm button (D6: words never confirm), a no word
    /// cancels the card, and anything else cancels it and goes on as a new request. A message that answers a typed
    /// question (<c>which_amount</c> or <c>which_contact</c> with no options) fills that slot, unless it is a sentence of
    /// its own. A parser that is unavailable gives <c>model_unavailable</c>.
    /// </summary>
    public async Task SendAsync(string typedText)
    {
        ArgumentNullException.ThrowIfNull(typedText);
        if (!await EnterAsync())
            return;
        try
        {
            SetBusy(true);
            var token = stopping;
            trace.Record(TraceZone.User, "message_received", FormattableString.Invariant($"length {typedText.Length}"));
            Add(new ConversationMessage(ConversationRole.User, ConversationKind.Typed));

            var pendingNote = PendingNote();
            var normalized = ArabicText.Normalize(typedText);
            var isYes = IsWord(normalized, yesWords);
            var isNo = IsWord(normalized, noWords);

            if (IsCardLive)
            {
                if (isYes)
                {
                    // D6: a typed yes never confirms; it points to the button.
                    trace.Record(TraceZone.Code, "points_to_confirm", null);
                    Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Info, ReasonCodes.PointsToConfirm));
                    return;
                }

                await CancelCardAsync(token);
                if (isNo)
                    return;
            }

            ParsedRequest parsed;
            try
            {
                parsed = await parser.ParseAsync(typedText, new ParseContext(pendingNote), token);
            }
            catch (ParserUnavailableException)
            {
                trace.Record(TraceZone.Model, "parse:unavailable", null);
                LastReply = new AgentReply.Refusal(ReasonCodes.ModelUnavailable, null, null);
                AddRefusal(ReasonCodes.ModelUnavailable);
                return;
            }

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
            // The conversation ended; the agent has recorded what it could (see Dispose).
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

            // Added while the question is still open, so the screen can show the option's own text.
            Add(new ConversationMessage(ConversationRole.User, ConversationKind.Option, option.Value));
            var request = await RequestForOptionAsync(question.Kind, option, lastRequest, token);
            if (request is null)
            {
                AddRefusal(ReasonCodes.NotDoneNothingTaken);
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
            // The conversation ended; the agent has recorded what it could (see Dispose).
        }
        finally
        {
            Release();
        }
    }

    /// <summary>The consent button: records consent in the wallet, then answers the last request again.</summary>
    public async Task GiveConsentAsync()
    {
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
                AddRefusal(ReasonCodes.NotDoneNothingTaken);
                return;
            }

            trace.Record(TraceZone.Code, "consent_given", null);
            ActionMessageIndex = null;
            if (lastRequest is not null)
                await HandleCoreAsync(lastRequest, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The conversation ended; the agent has recorded what it could (see Dispose).
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// The card's Confirm button (rule 1): the only path to <see cref="ConfirmHandler.ConfirmAsync"/>, with the PIN typed
    /// on the card. The PIN is passed through and kept nowhere. Returns the outcome, or null when no card is shown or the
    /// outcome is unknown after an exception.
    /// </summary>
    public async Task<ConfirmOutcome?> ConfirmAsync(string? pin)
    {
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
            // Ended mid-call: the handler has taken its cancellation path (back to Pending or Checking).
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
            // Ended mid-call: the handler has taken its cancellation path (back to Pending or Checking).
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
            // The conversation ended; the agent has recorded what it could (see Dispose).
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// Called when the card's countdown reaches zero (or the runner waits out the timer): reads the record again and
    /// marks the card expired when its timer has ended, so Confirm is no longer offered.
    /// </summary>
    public async Task RefreshAsync()
    {
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
            // The conversation ended; the agent has recorded what it could (see Dispose).
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// Ends the conversation: cancels the token every running action uses, so the agent takes its cancellation path (a
    /// Confirm cancelled before the send goes back to Pending, during it to Checking). The semaphore is deliberately not
    /// disposed: an action still running releases it in its <c>finally</c>, and a disposed semaphore would throw there.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    /// <summary>Waits for the conversation's turn; false when the conversation ended while waiting.</summary>
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
        QuestionMessageIndex = null;
        typedSlot = null;

        AgentReply reply;
        await using (var scope = scopes.CreateAsyncScope())
            reply = await scope.ServiceProvider.GetRequiredService<Orchestrator>().HandleAsync(userId, request, token);

        LastReply = reply;
        switch (reply)
        {
            case AgentReply.Refusal refusal:
                AddRefusal(refusal.Code, refusal.AmountIqd);
                break;

            case AgentReply.Question question:
                Question = question;
                lock (messagesGate)
                    questionsAsked.Add(new ConversationQuestion(question.Kind, question.Answer, [.. question.Options.Select(o => o.Value)]));
                QuestionMessageIndex = Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Question, question.Kind));
                if (question.Answer == QuestionAnswer.Typed)
                    typedSlot = question.Kind;
                break;

            case AgentReply.BalanceReply balance:
                Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Balance, AmountIqd: balance.BalanceIqd));
                break;

            case AgentReply.ConsentScreen:
                ConsentShown = true;
                ActionMessageIndex = Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Consent));
                break;

            case AgentReply.CardIssued issued:
                Card = issued.View;
                CardState = CardStatus.Live;
                PinVisible = Card.PinRequired;
                lock (messagesGate)
                    cardsIssued.Add(Card);
                CardMessageIndex = Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Card));
                break;

            case AgentReply.Unsupported unsupported:
                Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Unsupported, unsupported.Reason));
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

    private void ShowOutcome(ConfirmOutcome outcome)
    {
        switch (outcome)
        {
            case ConfirmOutcome.Done done:
                CardState = CardStatus.Done;
                PinVisible = false;
                ActionMessageIndex = null;
                WalletVersion++;
                AddOutcome(OutcomeCodes.Done, done.BalanceAfterIqd, done.Reference, done.PurchaseCode);
                break;

            case ConfirmOutcome.NotDone notDone:
                CardState = CardStatus.NotDone;
                PinVisible = false;
                ActionMessageIndex = null;
                Add(new ConversationMessage(
                    ConversationRole.Agent, ConversationKind.Outcome, OutcomeCodes.NotDone, notDone.AmountIqd, Reason: notDone.Reason));
                break;

            case ConfirmOutcome.Checking:
                CardState = CardStatus.Checking;
                PinVisible = false;
                ActionMessageIndex = AddOutcome(OutcomeCodes.Checking);
                break;

            case ConfirmOutcome.Expired:
                MarkExpired();
                break;

            case ConfirmOutcome.AlreadyHandled handled:
                if (CardState == CardStatus.Live)
                    CardState = StatusOf(handled.State) ?? CardState;
                AddOutcome(OutcomeCodes.AlreadyHandled);
                break;

            case ConfirmOutcome.NotFound:
                AddOutcome(OutcomeCodes.NotFound);
                break;

            case ConfirmOutcome.PinRequired:
                PinVisible = true;
                AddOutcome(OutcomeCodes.PinRequired);
                break;

            case ConfirmOutcome.PinWrong wrong:
                PinVisible = true;
                Add(new ConversationMessage(
                    ConversationRole.Agent, ConversationKind.Outcome, OutcomeCodes.PinWrong, AttemptsLeft: wrong.AttemptsLeft));
                break;

            case ConfirmOutcome.PinMalformed:
                PinVisible = true;
                AddOutcome(OutcomeCodes.PinMalformed);
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
            AddRefusal(ReasonCodes.NotDoneNothingTaken);
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
            AddOutcome(OutcomeCodes.Cancelled);
            return;
        }

        // The record left Pending meanwhile (its timer ended): show that instead.
        MarkExpired();
    }

    private void MarkExpired()
    {
        CardState = CardStatus.Expired;
        PinVisible = false;
        AddOutcome(OutcomeCodes.Expired);
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

    private static string[] NormalizeWords(string[]? words)
        => words is null
            ? []
            : [.. words.Select(ArabicText.Normalize).Where(w => w.Length > 0).Distinct(StringComparer.Ordinal)];

    private static bool IsWord(string normalized, string[] words)
        => normalized.Length > 0 && Array.Exists(words, w => string.Equals(normalized, w, StringComparison.Ordinal));

    private void AddRefusal(string code, long? amountIqd = null)
        => Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, code, amountIqd));

    private int AddOutcome(string code, long? amountIqd = null, string? reference = null, string? purchaseCode = null)
        => Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Outcome, code, amountIqd, reference, purchaseCode));

    /// <summary>Adds a message and raises <see cref="Changed"/>; returns its position in <see cref="Messages"/>.</summary>
    private int Add(ConversationMessage message)
    {
        int index;
        lock (messagesGate)
        {
            index = messages.Count;
            messages.Add(message);
        }

        Changed?.Invoke();
        return index;
    }

    /// <summary>An action that failed for a reason other than the wallet's answer: no card, nothing taken.</summary>
    private void Failed(Exception ex)
    {
        trace.Record(TraceZone.Code, "error", ex.GetType().Name);
        AddRefusal(ReasonCodes.NotDoneNothingTaken);
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        Changed?.Invoke();
    }

    /// <summary>Ends the action: the last <see cref="Changed"/> is raised before the turn is given up.</summary>
    private void Release()
    {
        Busy = false;
        try
        {
            Changed?.Invoke();
        }
        finally
        {
            gate.Release();
        }
    }
}
