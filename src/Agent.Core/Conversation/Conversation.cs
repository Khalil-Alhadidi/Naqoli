using System.Globalization;
using Agent.Core.Confirm;
using Agent.Core.Learning;
using Agent.Core.Masking;
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
    private readonly string[][] yesEntries;
    private readonly string[][] noEntries;
    private readonly IOptionsMonitor<AgentOptions> options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken stopping;
    private readonly Lock messagesGate = new();
    private readonly List<ConversationMessage> messages = [];
    private readonly List<CardView> cardsIssued = [];
    private readonly List<ConversationQuestion> questionsAsked = [];

    // The pending request (D3): the request as parsed or merged, and what the user's taps have answered for it.
    private ParsedRequest? lastRequest;
    private SlotAnswers answers = SlotAnswers.None;
    private string? typedSlot;
    private string? lastMaskedText;
    private bool disposed;

    /// <param name="scopes">Opens one scope per action.</param>
    /// <param name="parser">The parse call (rule 2).</param>
    /// <param name="clock">The agent's clock; the card's timer is measured against it.</param>
    /// <param name="options">
    /// The agent's options. <see cref="AgentOptions.YesWords"/> and <see cref="AgentOptions.NoWords"/> are read once here;
    /// <see cref="AgentOptions.MaxYesNoWords"/> is read again for every message, so a value saved in the settings applies
    /// to the next message of an open conversation.
    /// </param>
    /// <param name="trace">Where this conversation's own steps go (the services resolved per action write to the scope's sink).</param>
    /// <param name="userId">The wallet user the conversation acts for.</param>
    public Conversation(
        IServiceScopeFactory scopes,
        IParser parser,
        TimeProvider clock,
        IOptionsMonitor<AgentOptions> options,
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
        this.options = options;
        var current = options.CurrentValue;
        yesEntries = Entries(current.YesWords);
        noEntries = Entries(current.NoWords);
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

    /// <summary>What the user's taps have answered for <see cref="LastRequest"/> (built by code; never sent to the parse call).</summary>
    public SlotAnswers LastAnswers => answers;

    /// <summary>
    /// The learning-log entry of <see cref="LastReply"/>, as the conversation wrote it (<c>model_invalid_reply</c> for a
    /// structure the validator refused, else <see cref="LearningLogRule.EntryFor"/>), or null when nothing was logged.
    /// The runner grades this entry.
    /// </summary>
    public LearningEntry? LastLearningEntry { get; private set; }

    /// <summary>True once the consent screen has been shown in this conversation.</summary>
    public bool ConsentShown { get; private set; }

    /// <summary>
    /// A typed message. With a live card, a yes word points to the Confirm button (D6: words never confirm), a no word
    /// cancels the card, and anything else cancels it and goes on as a new request. While a typed question is open
    /// (<c>which_amount</c> or <c>which_contact</c> with no options), a message the parse call reads as unclear fills
    /// that slot from the typed text, a transfer that is itself missing a recipient or an amount merges into the pending
    /// transfer, and anything else (a complete request, another intent) replaces the pending request. A parser that is
    /// unavailable gives <c>model_unavailable</c>.
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
            // D6: a yes or a no is a message of at most MaxYesNoWords words made only of whole yes (or no) entries; a
            // sentence with any other word in it is a request.
            var words = WordsOf(typedText);
            var maxYesNoWords = options.CurrentValue.MaxYesNoWords;
            var isYes = IsPhrase(words, yesEntries, maxYesNoWords);
            var isNo = !isYes && IsPhrase(words, noEntries, maxYesNoWords);

            var cardWaiting = IsCardLive;
            if (cardWaiting)
            {
                if (isYes)
                {
                    // D6: a typed yes never confirms; it points to the button.
                    trace.Record(TraceZone.Code, "points_to_confirm", null);
                    Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Info, ReasonCodes.PointsToConfirm));
                    return;
                }

                if (isNo)
                {
                    await CancelCardAsync(token);
                    return;
                }

                // Anything else is parsed first, with the card still waiting (the pending note says so): only a new
                // request cancels it (below).
            }

            // Rule 5: the parse call sees the typed text with every number masked, and nothing else of the user's.
            var masked = Masker.Mask(typedText);
            lastMaskedText = masked.Text;

            ParsedRequest parsed;
            try
            {
                parsed = await parser.ParseAsync(masked.Text, new ParseContext(pendingNote, masked.Map), token);
            }
            catch (ParserUnavailableException)
            {
                // Nothing was understood, so a waiting card stays until its timer ends.
                trace.Record(TraceZone.Model, "parse:unavailable", null);
                if (cardWaiting)
                    trace.Record(TraceZone.Code, CardKeptStep, ReasonCodes.ModelUnavailable);
                LastReply = new AgentReply.Refusal(ReasonCodes.ModelUnavailable, null, null);
                LastLearningEntry = null;
                AddRefusal(ReasonCodes.ModelUnavailable);
                return;
            }

            trace.Record(TraceZone.Model, "parse:" + parser.Name, parsed.Intent.ToString());
            parsed = Unmask(parsed, masked.Map);

            // A structure the validator refused: the reply is not_understood, and the log says why (by field name).
            LearningEntry? invalidReply = null;
            if (parsed is { Intent: RequestIntent.Unclear, Problem: { } problem })
            {
                trace.Record(TraceZone.Code, LearningLogRule.ModelInvalidReply, problem);
                invalidReply = new LearningEntry(LearningLogRule.ModelInvalidReply, problem);
            }

            if (cardWaiting)
            {
                // D6: anything unclear leaves the card until its timer ends, and so does a request to change it (its
                // reply tells the user to cancel and ask again). Any other request cancels the card and goes on as new.
                if (CardKeptBecause(parsed) is { } reason)
                {
                    trace.Record(TraceZone.Code, CardKeptStep, reason);
                    await HandleCoreAsync(parsed, SlotAnswers.None, token, invalidReply);
                    return;
                }

                await CancelCardAsync(token);
            }

            var request = parsed;
            var kept = SlotAnswers.None;
            if (typedSlot is not null && lastRequest is not null)
            {
                if (parsed.Intent == RequestIntent.Unclear)
                {
                    if (!isYes && !isNo && FillTypedSlot(lastRequest, typedSlot, typedText) is { } filled)
                    {
                        request = filled;
                        kept = answers;
                    }
                }
                else if (MergePartial(lastRequest, parsed) is { } merged)
                {
                    // D3: the answer to a typed question that the model read as a partial request of the same kind.
                    trace.Record(TraceZone.Code, "answer_merged", typedSlot);
                    request = merged;
                    kept = answers;
                }
            }

            await HandleCoreAsync(request, kept, token, ReferenceEquals(request, parsed) ? invalidReply : null);
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
    /// <c>option_rejected</c>). The tap answers the pending request (D3): a contact, bill or product option is kept by
    /// its value in <see cref="SlotAnswers"/>, so the orchestrator picks exactly that one among what it resolves; an
    /// amount or operator option fills the request's own slot. Nothing is read from the wallet here.
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
            if (AnswerForOption(question.Kind, option, lastRequest, answers) is not { } answered)
            {
                AddRefusal(ReasonCodes.NotDoneNothingTaken);
                return;
            }

            await HandleCoreAsync(answered.Request, answered.Answers, token);
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
    /// The consent button: records consent in the wallet, then answers the pending request again, with what taps have
    /// answered for it. (The consent gate comes before any question, so a pending request normally has no answers here;
    /// they are passed all the same.)
    /// </summary>
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
                await HandleCoreAsync(lastRequest, answers, token);
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
    /// The card's "Try again" or "Send again" button (D19): rebuilds the card shown from its stored record through
    /// <see cref="Orchestrator.ReissueAsync"/>, without the model, and shows the reply as any reply (a new card, or a
    /// refusal that leaves the old card as it is). Try again is for a card that expired, was cancelled or definitely
    /// failed; Send again for a done one. With no card, a live card or a payment still checking it does nothing (traced
    /// as <c>reissue_rejected</c>). It never reaches the Confirm handler: the new card waits for its own Confirm.
    /// </summary>
    public async Task ReissueAsync()
    {
        if (!await EnterAsync())
            return;
        try
        {
            SetBusy(true);
            var token = stopping;
            trace.Record(TraceZone.User, "reissue_tapped", null);
            if (Card is not { } card || ReissueKindOf(CardState) is not { } kind)
            {
                trace.Record(TraceZone.Code, ReissueRejectedStep, CardState?.ToString());
                return;
            }

            PendingRecord? record;
            await using (var scope = scopes.CreateAsyncScope())
                record = await scope.ServiceProvider.GetRequiredService<PendingStore>().FindAsync(card.PendingId, userId, token);

            // The stored record has the last word: one that is still open (live, executing or checking) is not rebuilt.
            if (record is null
                || record.State is PendingState.Executing or PendingState.Checking
                || (record.State == PendingState.Pending && record.ExpiresAt > clock.GetUtcNow()))
            {
                trace.Record(TraceZone.Code, ReissueRejectedStep, record?.State.ToString());
                return;
            }

            Question = null;
            QuestionMessageIndex = null;
            typedSlot = null;

            AgentReply reply;
            await using (var scope = scopes.CreateAsyncScope())
                reply = await scope.ServiceProvider.GetRequiredService<Orchestrator>().ReissueAsync(userId, record, kind, token);

            LastReply = reply;
            LastLearningEntry = null;
            ShowReply(reply);
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

    /// <summary>The trace step of a reissue tap that rebuilt nothing; the detail is the card's or the record's state.</summary>
    private const string ReissueRejectedStep = "reissue_rejected";

    /// <summary>
    /// D19: what a card in <paramref name="state"/> offers. Expired, Cancelled and NotDone (nothing moved) offer
    /// <see cref="ReissueKind.TryAgain"/>; Done offers <see cref="ReissueKind.SendAgain"/>; a live card, a payment still
    /// checking and no card offer neither (null).
    /// </summary>
    public static ReissueKind? ReissueKindOf(CardStatus? state) => state switch
    {
        CardStatus.Expired or CardStatus.Cancelled or CardStatus.NotDone => ReissueKind.TryAgain,
        CardStatus.Done => ReissueKind.SendAgain,
        _ => null,
    };

    /// <summary>The trace step of a message that left a waiting card in place; the detail says why.</summary>
    private const string CardKeptStep = "card_kept";

    /// <summary>
    /// Why a message typed while a card waits leaves it (D6): <c>unclear</c> (including a structure the validator
    /// refused), or <c>change_waiting_card</c>; null when the message is a new request, which cancels the card.
    /// </summary>
    private static string? CardKeptBecause(ParsedRequest parsed) => parsed switch
    {
        { Intent: RequestIntent.Unclear } => "unclear",
        { Intent: RequestIntent.Unsupported, UnsupportedReason: LearningLogRule.ChangeWaitingCard } => LearningLogRule.ChangeWaitingCard,
        _ => null,
    };

    /// <summary>D5: the one-line note of what is pending, from the type and codes only (rule 5).</summary>
    private string? PendingNote()
    {
        if (IsCardLive && Card is { } card)
            return "pending_card " + card.Type;
        if (Question is { } question)
            return "open_question " + question.Kind;
        return null;
    }

    /// <summary>
    /// Makes (<paramref name="request"/>, <paramref name="tapped"/>) the pending request, answers it and shows the reply,
    /// then writes its learning-log entry: <paramref name="invalidReply"/> when the parse call's structure was refused,
    /// else what <see cref="LearningLogRule.EntryFor"/> returns.
    /// </summary>
    private async Task HandleCoreAsync(
        ParsedRequest request, SlotAnswers tapped, CancellationToken token, LearningEntry? invalidReply = null)
    {
        lastRequest = request;
        answers = tapped;
        Question = null;
        QuestionMessageIndex = null;
        typedSlot = null;

        AgentReply reply;
        await using (var scope = scopes.CreateAsyncScope())
            reply = await scope.ServiceProvider.GetRequiredService<Orchestrator>().HandleAsync(userId, request, tapped, token);

        LastReply = reply;
        ShowReply(reply);
        LastLearningEntry = invalidReply ?? LearningLogRule.EntryFor(reply, request);
        await WriteLearningAsync(LastLearningEntry, token);
    }

    /// <summary>
    /// Writes <paramref name="entry"/> with the masked text of the last typed message (rule 5: never the typed text).
    /// The reply is already shown, so a failed write is traced and does not change it.
    /// </summary>
    private async Task WriteLearningAsync(LearningEntry? entry, CancellationToken token)
    {
        if (entry is null)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LearningLog>().WriteAsync(userId, entry, lastMaskedText ?? string.Empty, token);
            trace.Record(TraceZone.Code, "learning_logged", entry.Reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            trace.Record(TraceZone.Code, "error", ex.GetType().Name);
        }
    }

    /// <summary>
    /// The request with a recipient that is exactly a token of this message's <paramref name="map"/> turned back into
    /// the number as typed; any other token stays a token.
    /// </summary>
    private static ParsedRequest Unmask(ParsedRequest parsed, MaskMap map)
        => parsed.RecipientText is { } recipient && map.TryUnmask(recipient.Trim(), out var original)
            ? parsed with { RecipientText = original }
            : parsed;

    private void ShowReply(AgentReply reply)
    {
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
                // A known gap's note comes before the card, as an informational reply (account_choice_unavailable).
                if (issued.Note is { } note)
                    Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Info, note));
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

    /// <summary>
    /// The transfer a typed answer completes: when a typed question is open on a pending transfer and the parse call read
    /// the answer as a transfer that is itself missing the recipient or the amount, the pending transfer with every slot
    /// the answer names put in (the recipient; the amount with its unit; the currency). Null for anything else, which
    /// then replaces the pending request: a complete transfer, another intent, the balance, an unsupported request.
    /// </summary>
    private static ParsedRequest? MergePartial(ParsedRequest pending, ParsedRequest parsed)
    {
        if (pending.Intent != RequestIntent.Transfer || parsed.Intent != RequestIntent.Transfer)
            return null;
        if (!string.IsNullOrWhiteSpace(parsed.RecipientText) && parsed.AmountValue is not null)
            return null;

        var merged = pending;
        if (!string.IsNullOrWhiteSpace(parsed.RecipientText))
            merged = merged with { RecipientText = parsed.RecipientText };
        if (parsed.AmountValue is not null)
            merged = merged with { AmountValue = parsed.AmountValue, AmountUnit = parsed.AmountUnit };
        if (parsed.CurrencyText is not null)
            merged = merged with { CurrencyText = parsed.CurrencyText };
        return merged;
    }

    /// <summary>
    /// The pending request and answers a tapped option stands for (D3), built from the option's value alone, with no
    /// wallet read: the orchestrator checks the value against what it resolves. Null for a question kind with no tap.
    /// </summary>
    private static (ParsedRequest Request, SlotAnswers Answers)? AnswerForOption(
        string kind, Option option, ParsedRequest last, SlotAnswers answers)
    {
        switch (kind)
        {
            case QuestionKinds.WhichContact:
            case QuestionKinds.DidYouMean:
                return (last, answers with { ContactOption = option.Value });

            case QuestionKinds.WhichAmount:
                return (last with { AmountValue = option.AmountIqd, AmountUnit = AmountUnit.Dinars }, answers);

            case QuestionKinds.WhichOperator:
                return (last with { OperatorText = option.Value }, answers);

            case QuestionKinds.WhichProduct:
                {
                    // Rule 17: a bill request that led to the store becomes a purchase of an internet card.
                    var purchase = last.Intent == RequestIntent.Purchase
                        ? last
                        : ParsedRequest.Purchase(null, last.ProductType ?? BillResolver.InternetProductType, null);
                    return (purchase, answers with { ProductCode = option.Value });
                }

            case QuestionKinds.WhichBill:
            case QuestionKinds.BillOrStore:
                {
                    // Rule 13: the store option is a purchase of an internet card, which asks for the operator (rule 11).
                    if (option.Value == Option.StoreInternet)
                        return (ParsedRequest.Purchase(null, BillResolver.InternetProductType, null), SlotAnswers.None);

                    var bill = last.Intent == RequestIntent.Bill ? last : ParsedRequest.Bill(null, null, null, AmountUnit.None);
                    return (bill, answers with { BillCode = option.Value });
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

    /// <summary>
    /// The D6 entries of a configured list, each as its sequence of words in the form <see cref="WordsOf"/> gives,
    /// longest first (so the longest entry is tried first at each position), without empty or repeated entries.
    /// </summary>
    private static string[][] Entries(string[]? entries)
        => entries is null
            ? []
            : [.. entries
                .Select(WordsOf)
                .Where(words => words.Length > 0)
                .DistinctBy(words => string.Join(' ', words), StringComparer.Ordinal)
                .OrderByDescending(words => words.Length)];

    /// <summary>
    /// The words in which a typed message and a D6 entry are compared: <see cref="ArabicText.Normalize"/> (alef and digit
    /// forms, harakat), then every punctuation or symbol character, anywhere, replaced by a space (a question mark, a
    /// comma, a full stop, in either script), then Latin letters lower-cased, split on spaces.
    /// </summary>
    internal static string[] WordsOf(string text)
    {
        var normalized = ArabicText.Normalize(text);
        var spaced = string.Create(normalized.Length, normalized, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = char.IsPunctuation(c) || char.IsSymbol(c) ? ' ' : c;
            }
        });
        return spaced.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// D6 (Khalil, 15 Oct 2026): true when the message has one to <paramref name="maxWords"/> words and its words can be
    /// cut, left to right, into whole entries of <paramref name="entries"/>: at each position the longest entry that
    /// fits is taken, and a position where none fits means no. A multi-word entry matches only as the whole phrase,
    /// never one of its words alone, and a word that merely contains an entry is not one.
    /// </summary>
    private static bool IsPhrase(string[] words, string[][] entries, int maxWords)
    {
        if (words.Length < 1 || words.Length > maxWords)
            return false;

        var position = 0;
        while (position < words.Length)
        {
            var at = position;
            var entry = Array.Find(entries, e => e.Length <= words.Length - at && e.AsSpan().SequenceEqual(words.AsSpan(at, e.Length)));
            if (entry is null)
                return false;
            position += entry.Length;
        }

        return true;
    }

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
