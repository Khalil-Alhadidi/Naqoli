using Agent.Core.Aliases;
using Agent.Core.Audit;
using Agent.Core.Confirm;
using Agent.Core.Learning;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Store;
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

    // True once OpenAsync has run its check; a second call returns at once.
    private bool opened;

    // Ruling 3 (milestone 9a): the recipient of lastRequest occurs verbatim in a message the user typed (an answer the
    // code read, or the model's recipient_text found inside the typed text). Only then is an alias offered.
    private bool recipientVerbatim;

    // The alias the card shown would offer once its payment is done (rule 18); kept with the card, never stored.
    private AliasOffer? cardAlias;

    // D6 extension (Khalil, 17 Oct 2026): the last request was a transfer refused for an amount reason, so the next
    // typed message may be a new amount for it (one message only; see AmountRetryCodes).
    private bool amountRetryOpen;

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

    /// <summary>
    /// Goes up after every Done outcome and every alias saved or deleted, so a wallet view knows to reload;
    /// <see cref="LastWalletChange"/> says which.
    /// </summary>
    public int WalletVersion { get; private set; }

    /// <summary>What the last rise of <see cref="WalletVersion"/> changed; set together with it (<see cref="WalletChanged"/>).</summary>
    public ConversationWalletChange LastWalletChange { get; private set; }

    /// <summary>
    /// The open offer to save an alias (rule 18, milestone 9a), shown under the Done outcome of a transfer whose recipient
    /// needed a question; null when none is open. Cleared by the next typed message, tap, reissue or save.
    /// </summary>
    public AliasOffer? AliasOffer { get; private set; }

    /// <summary>The position in <see cref="Messages"/> of the <see cref="ConversationKind.AliasOffer"/> message of <see cref="AliasOffer"/>.</summary>
    public int? AliasOfferMessageIndex { get; private set; }

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
    /// structure the validator refused, <c>model_refusal</c> for a message the model refused to parse, else
    /// <see cref="LearningLogRule.EntryFor"/>), or null when nothing was logged.
    /// The runner grades this entry.
    /// </summary>
    public LearningEntry? LastLearningEntry { get; private set; }

    /// <summary>Set for the typed message being handled when it is four to six digits only: no learning row is written.</summary>
    private bool suppressLearningRow;

    /// <summary>True once the consent screen has been shown in this conversation.</summary>
    public bool ConsentShown { get; private set; }

    /// <summary>
    /// A typed message. With a live card, a yes word points to the Confirm button (D6: words never confirm), a no word
    /// cancels the card, and anything else cancels it and goes on as a new request. While a question is open, a message
    /// that is not a yes or a no is first read in code, before any masking or parse call (17 Oct 2026): an answer to
    /// <c>which_amount</c> by <see cref="AmountText.TryRead"/>, an answer to <c>which_contact</c> or <c>did_you_mean</c>
    /// by <see cref="ContactResolver.ResolveAnswer"/> over the user's contacts; a hit fills the slot and the model is
    /// never called; filler words at either end of the answer are dropped first (<see cref="AgentOptions.FillerWords"/>).
    /// After a transfer refused for an amount reason (<c>insufficient_balance</c>, <c>over_per_transaction_cap</c>,
    /// <c>over_daily_cap</c>, <c>amount_below_minimum</c>; D6 extension, 17 Oct 2026), the next message only, when it
    /// reads as an amount, goes into that transfer (trace <c>amount_retry</c>) and the checks run again; anything else
    /// drops it and is handled as a fresh message. Only what the code cannot read goes to the parse call. Then, while a
    /// question is open, a message the parse call reads as unclear repeats the question and leaves it open
    /// (<c>not_understood</c> with the question's kind); while a typed question is open, a transfer that is itself
    /// missing a recipient or an amount merges into the pending transfer; anything else (a complete request, another
    /// intent) replaces the pending request. A parser that is unavailable gives <c>model_unavailable</c>.
    /// </summary>
    public async Task SendAsync(string typedText)
    {
        ArgumentNullException.ThrowIfNull(typedText);
        if (!await EnterAsync())
            return;
        try
        {
            SetBusy(true);
            ClearAliasOffer();
            var token = stopping;

            // Known gaps row 34 (Khalil, 19 Oct 2026): a message that is only four to six digits may be a PIN, so it
            // never writes a learning row, whatever path it takes.
            var digitsOnly = DigitsOnly(typedText);
            suppressLearningRow = digitsOnly;
            trace.Record(TraceZone.User, "message_received", FormattableString.Invariant($"length {typedText.Length}"));
            Add(new ConversationMessage(ConversationRole.User, ConversationKind.Typed));

            // Known gaps row 36 (Khalil, 19 Oct 2026): a message longer than MaxMessageChars is not read. Nothing is
            // masked, the parser is not called and no learning row is written; a waiting card stays (unclear, D6).
            if (typedText.Length > options.CurrentValue.MaxMessageChars)
            {
                trace.Record(TraceZone.Code, TraceSteps.MessageTooLong, FormattableString.Invariant($"{typedText.Length}"));
                LastReply = new AgentReply.Refusal(ReasonCodes.NotUnderstood, null, null);
                LastLearningEntry = null;
                AddRefusal(ReasonCodes.NotUnderstood);
                return;
            }

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

                // Known gaps row 34: a PIN typed into the chat while the PIN field shows never reaches the parse call;
                // the reply points to the PIN field and the card stays live.
                if (PinVisible && digitsOnly)
                {
                    trace.Record(TraceZone.Code, TraceSteps.PinTypedInChat, null);
                    AddOutcome(OutcomeCodes.PinRequired);
                    LastLearningEntry = null;
                    return;
                }

                // Anything else is parsed first, with the card still waiting (the pending note says so): only a new
                // request cancels it (below).
            }

            // D6 extension (17 Oct 2026): after a transfer refused for an amount reason, the next message only, if it
            // reads as an amount, goes into that transfer and the checks run again; anything else is a fresh message.
            if (amountRetryOpen)
            {
                amountRetryOpen = false;
                if (!cardWaiting && !isYes && !isNo && lastRequest is { } refused
                    && WithoutFillerWords(typedText, options.CurrentValue.FillerWords) is { } amountText
                    && AmountText.TryRead(amountText, out var retryValue, out var retryUnit))
                {
                    trace.Record(TraceZone.Code, AmountRetryStep, null);
                    lastMaskedText = Masker.Mask(typedText).Text;
                    await HandleCoreAsync(refused with { AmountValue = retryValue, AmountUnit = retryUnit }, answers, token);
                    return;
                }
            }

            // An answer to the open question is read in code first; only what the code cannot read reaches the model.
            if (!cardWaiting && !isYes && !isNo && Question is { } open && lastRequest is not null
                && await AnswerInCodeAsync(open.Kind, lastRequest, typedText, token) is { } answered)
            {
                trace.Record(TraceZone.Code, AnswerParsedStep, open.Kind);
                lastMaskedText = Masker.Mask(typedText).Text;

                // A contact answer the code read is the user's own text (ruling 3); an amount answer keeps the flag.
                bool? verbatim = open.Kind is QuestionKinds.WhichContact or QuestionKinds.DidYouMean ? true : null;
                await HandleCoreAsync(answered.Request, answered.Answers, token, verbatim: verbatim);
                return;
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

            // Ruling 3: the model's recipient counts as the user's words only when it occurs in the typed text as it is.
            var parsedVerbatim = parsed.RecipientText is { } recipient
                && typedText.Contains(recipient.Trim(), StringComparison.Ordinal);

            // A structure the validator refused: the reply is not_understood, and the log says why (by field name).
            // A message the model refused to parse is its own path: the same reply, logged as model_refusal, no detail.
            LearningEntry? invalidReply = null;
            if (parsed is { Intent: RequestIntent.Unclear, Problem: ClaudeParser.RefusalProblem })
            {
                trace.Record(TraceZone.Code, LearningLogRule.ModelRefusal, null);
                invalidReply = new LearningEntry(LearningLogRule.ModelRefusal, null);
            }
            else if (parsed is { Intent: RequestIntent.Unclear, Problem: { } problem })
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
                    await HandleCoreAsync(parsed, SlotAnswers.None, token, invalidReply, parsedVerbatim);
                    return;
                }

                await CancelCardAsync(token);
            }

            // Not understood while a question is open: the question is asked again and stays open.
            if (Question is { } pending && parsed.Intent == RequestIntent.Unclear)
            {
                await RepeatQuestionAsync(pending.Kind, invalidReply, token);
                return;
            }

            var request = parsed;
            var kept = SlotAnswers.None;
            var verbatimNow = parsedVerbatim;
            if (typedSlot is not null && lastRequest is not null && MergePartial(lastRequest, parsed) is { } merged)
            {
                // D3: the answer to a typed question that the model read as a partial request of the same kind.
                trace.Record(TraceZone.Code, "answer_merged", typedSlot);
                request = merged;
                kept = answers;

                // The flag of whichever message supplied the recipient.
                verbatimNow = string.IsNullOrWhiteSpace(parsed.RecipientText) ? recipientVerbatim : parsedVerbatim;
            }

            await HandleCoreAsync(request, kept, token, ReferenceEquals(request, parsed) ? invalidReply : null, verbatimNow);
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
            suppressLearningRow = false;
            Release();
        }
    }

    /// <summary>The fewest and the most digits of a message that may be a PIN (known gaps row 34).</summary>
    private const int PinLikeMinDigits = 4;
    private const int PinLikeMaxDigits = 6;

    /// <summary>
    /// True when <paramref name="text"/>, trimmed, is only four to six digits (ASCII, Arabic-Indic or Eastern
    /// Arabic-Indic) with nothing else: a message that may be a PIN.
    /// </summary>
    private static bool DigitsOnly(string text)
    {
        var digits = ArabicDigits.ToAscii(text).Trim();
        return digits.Length is >= PinLikeMinDigits and <= PinLikeMaxDigits && digits.All(char.IsAsciiDigit);
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
            ClearAliasOffer();
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
    /// The final option of the open question (17 Oct 2026), under the same gate as <see cref="TapOptionAsync"/>; with no
    /// open question it does nothing (traced as <c>option_rejected</c>). On <c>which_contact</c> or <c>did_you_mean</c>
    /// with options it is "none of these" (<see cref="Option.NoneOfThese"/>): the pending request goes back to the
    /// orchestrator with that answer, which refuses it <c>recipient_not_found</c>. On every other question, typed ones
    /// included, it is "cancel" (<see cref="Option.CancelQuestion"/>): the pending request is cleared, an audit row
    /// <c>refused</c> with <c>question_cancelled</c> is written, and the next message is a fresh request.
    /// </summary>
    public async Task DeclineQuestionAsync()
    {
        if (!await EnterAsync())
            return;
        try
        {
            SetBusy(true);
            ClearAliasOffer();
            var token = stopping;
            var question = Question;
            if (question is null || lastRequest is null)
            {
                trace.Record(TraceZone.Code, "option_rejected", null);
                return;
            }

            var noneOfThese = question.Options.Count > 0 && question.Kind is QuestionKinds.WhichContact or QuestionKinds.DidYouMean;

            // Added while the question is still open, as a tap is.
            Add(new ConversationMessage(
                ConversationRole.User, ConversationKind.Option, noneOfThese ? Option.NoneOfThese : Option.CancelQuestion));
            if (noneOfThese)
            {
                await HandleCoreAsync(lastRequest, answers with { ContactOption = Option.NoneOfThese }, token);
                return;
            }

            trace.Record(TraceZone.Code, "question_cancelled", question.Kind);
            await using (var scope = scopes.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AuditLog>().WriteRefusalAsync(userId, ReasonCodes.QuestionCancelled, null, token);

            lastRequest = null;
            answers = SlotAnswers.None;
            Question = null;
            QuestionMessageIndex = null;
            typedSlot = null;
            LastReply = new AgentReply.Refusal(ReasonCodes.QuestionCancelled, null, null);
            LastLearningEntry = null;
            AddRefusal(ReasonCodes.QuestionCancelled);
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
    /// The open check (milestone 7 post-tag; Khalil, 18 Oct 2026): run once when the phone opens, before any typing.
    /// Reads the user through <see cref="WalletClient.GetUserAsync"/>. Not consented: one <c>consent_shown</c> audit row,
    /// trace <c>consent_checked</c> <c>required</c>, and the consent message with its button (<see cref="ConsentShown"/>,
    /// <see cref="ActionMessageIndex"/>). Consented: trace <c>consent_checked</c> <c>given</c>, nothing shown. No usable
    /// answer: trace <c>consent_checked</c> <c>unavailable</c>, nothing shown, and the first request's consent gate (which
    /// is unchanged) catches it. A tap on the button with no pending request only records consent
    /// (<see cref="GiveConsentAsync"/>). A second call returns at once. Takes the turn like every action.
    /// </summary>
    public async Task OpenAsync()
    {
        if (opened || !await EnterAsync())
            return;
        try
        {
            if (opened)
                return;
            opened = true;
            SetBusy(true);
            var token = stopping;
            await using var scope = scopes.CreateAsyncScope();
            var read = await scope.ServiceProvider.GetRequiredService<WalletClient>().GetUserAsync(userId, token);
            switch (read)
            {
                case WalletResult<WalletUser>.Ok { Value.ConsentGiven: true }:
                    trace.Record(TraceZone.Code, TraceSteps.ConsentChecked, ConsentGivenDetail);
                    break;

                case WalletResult<WalletUser>.Ok:
                    await scope.ServiceProvider.GetRequiredService<AuditLog>().WriteAsync(
                        userId, null, AuditEvents.ConsentShown, null, null, null, null, null, token);
                    trace.Record(TraceZone.Code, TraceSteps.ConsentChecked, ConsentRequiredDetail);
                    ShowReply(new AgentReply.ConsentScreen());
                    break;

                default:
                    trace.Record(TraceZone.Code, TraceSteps.ConsentChecked, ConsentUnavailableDetail);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing is shown: the first request's consent gate still runs.
            trace.Record(TraceZone.Code, "error", ex.GetType().Name);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The conversation ended before the check finished.
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
            ClearAliasOffer();
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
    /// The Save button of the open alias offer (rule 18): saves <see cref="AliasOffer"/> through
    /// <see cref="AliasDirectory.SaveAsync"/>, the only way an offer is saved (never by words). With no open offer it does
    /// nothing and returns null (traced <c>option_rejected</c>). Saved: an <see cref="ConversationKind.AliasSaved"/>
    /// message and <see cref="WalletVersion"/> up. Refused: a refusal with its code. Either way the offer closes; a wallet
    /// that gave no answer adds <c>not_done_nothing_taken</c> and keeps the offer open, so the tap can be tried again.
    /// No money moves and the card's record is untouched.
    /// </summary>
    public async Task<AliasResult?> SaveAliasAsync()
    {
        if (!await EnterAsync())
            return null;
        AliasOffer? offer = null;
        try
        {
            SetBusy(true);
            offer = AliasOffer;
            ClearAliasOffer();
            if (offer is null)
            {
                trace.Record(TraceZone.Code, "option_rejected", null);
                return null;
            }

            trace.Record(TraceZone.User, "save_alias_tapped", null);
            AliasResult result;
            await using (var scope = scopes.CreateAsyncScope())
                result = await scope.ServiceProvider.GetRequiredService<AliasDirectory>().SaveAsync(userId, offer.Text, offer.AccountId, stopping);

            switch (result)
            {
                case AliasResult.Saved:
                    WalletChanged(ConversationWalletChange.Alias);
                    Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.AliasSaved));
                    break;
                case AliasResult.Refused refused:
                    AddRefusal(refused.Code);
                    break;
                case AliasResult.Unavailable:
                    // As a wallet read that gave no answer: nothing written; the offer stays open for another tap.
                    AddRefusal(ReasonCodes.NotDoneNothingTaken);
                    AliasOffer = offer;
                    AliasOfferMessageIndex = Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.AliasOffer));
                    break;
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Security review R1: nothing is known to be written, so the offer stays open, as on Unavailable.
            Failed(ex);
            if (offer is not null && AliasOffer is null)
            {
                AliasOffer = offer;
                AliasOfferMessageIndex = Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.AliasOffer));
            }

            return null;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            Release();
        }
    }

    /// <summary>The directory (milestone 9a): the user's contacts and aliases, under the conversation's turn.</summary>
    public async Task<WalletResult<WalletContacts>> ListAliasesAsync()
    {
        if (!await EnterAsync())
            return new WalletResult<WalletContacts>.Unavailable(StoppedReason);
        try
        {
            SetBusy(true);
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AliasDirectory>().ListAsync(userId, stopping);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Security review M1: a failure here (a torn-down scope, a DI error) is a read with no answer, never a
            // thrown exception that ends the circuit.
            trace.Record(TraceZone.Code, "error", ex.GetType().Name);
            return new WalletResult<WalletContacts>.Unavailable(ErrorReason);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return new WalletResult<WalletContacts>.Unavailable(StoppedReason);
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// My orders (milestone 9b): the user's store purchases with their codes (<see cref="OrdersReader.ListAsync"/>), under
    /// the conversation's turn. Adds no message and does not change <see cref="WalletVersion"/>.
    /// </summary>
    public async Task<WalletResult<IReadOnlyList<PurchaseOrder>>> ListOrdersAsync()
    {
        if (!await EnterAsync())
            return new WalletResult<IReadOnlyList<PurchaseOrder>>.Unavailable(StoppedReason);
        try
        {
            SetBusy(true);
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OrdersReader>().ListAsync(userId, stopping);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // As the directory read: a failure here is a read with no answer, never an exception that ends the circuit.
            trace.Record(TraceZone.Code, "error", ex.GetType().Name);
            return new WalletResult<IReadOnlyList<PurchaseOrder>>.Unavailable(ErrorReason);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return new WalletResult<IReadOnlyList<PurchaseOrder>>.Unavailable(StoppedReason);
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// The directory: adds an alias of one of the user's contacts or aliases (<see cref="AliasDirectory.SaveAsync"/>).
    /// Adds no message.
    /// </summary>
    public Task<AliasResult?> AddAliasAsync(string text, int targetAccountId)
    {
        ArgumentNullException.ThrowIfNull(text);
        return DirectoryAsync((directory, token) => directory.SaveAsync(userId, text, targetAccountId, token));
    }

    /// <summary>
    /// The directory: adds an alias of the account behind a typed number (<see cref="AliasDirectory.SaveForPhoneAsync"/>,
    /// D21). Adds no message.
    /// </summary>
    public Task<AliasResult?> AddAliasForPhoneAsync(string text, string phone)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(phone);
        return DirectoryAsync((directory, token) => directory.SaveForPhoneAsync(userId, text, phone, token));
    }

    /// <summary>The directory: deletes one of the user's aliases (<see cref="AliasDirectory.DeleteAsync"/>). Adds no message.</summary>
    public Task<AliasResult?> DeleteAliasAsync(int aliasId)
        => DirectoryAsync((directory, token) => directory.DeleteAsync(userId, aliasId, token));

    /// <summary>The reason of a directory read that the end of the conversation stopped.</summary>
    private const string StoppedReason = "stopped";

    /// <summary>The details of <c>consent_checked</c>: the orchestrator's <c>given</c> and <c>required</c>, and the open check's <c>unavailable</c>.</summary>
    private const string ConsentGivenDetail = "given";

    private const string ConsentRequiredDetail = "required";

    private const string ConsentUnavailableDetail = "unavailable";

    /// <summary>The reason of a directory read that failed for another reason (traced <c>error</c> with the type).</summary>
    private const string ErrorReason = "error";

    /// <summary>
    /// Runs one directory action under the conversation's turn, in its own scope; <see cref="WalletVersion"/> goes up when
    /// it saved or deleted. Null when the conversation ended or the action failed for another reason (traced <c>error</c>).
    /// </summary>
    private async Task<AliasResult?> DirectoryAsync(Func<AliasDirectory, CancellationToken, Task<AliasResult>> action)
    {
        if (!await EnterAsync())
            return null;
        try
        {
            SetBusy(true);
            AliasResult result;
            await using (var scope = scopes.CreateAsyncScope())
                result = await action(scope.ServiceProvider.GetRequiredService<AliasDirectory>(), stopping);

            if (result is AliasResult.Saved or AliasResult.Deleted)
                WalletChanged(ConversationWalletChange.Alias);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            trace.Record(TraceZone.Code, "error", ex.GetType().Name);
            return null;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return null;
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

    /// <summary>
    /// Records a wallet change: <see cref="LastWalletChange"/> first, then <see cref="WalletVersion"/> up, both before the
    /// <see cref="Changed"/> that follows, so a reader who sees the new version reads its kind.
    /// </summary>
    private void WalletChanged(ConversationWalletChange kind)
    {
        LastWalletChange = kind;
        WalletVersion++;
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

    /// <summary>The card type an alias is offered for.</summary>
    private const string TransferType = "transfer";

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
    /// then writes its learning-log entry: <paramref name="invalidReply"/> when the parse call's structure was refused
    /// (<c>model_invalid_reply</c>) or the model refused the message (<c>model_refusal</c>), else what
    /// <see cref="LearningLogRule.EntryFor"/> returns.
    /// </summary>
    /// <param name="verbatim">
    /// Whether the request's recipient occurs verbatim in a message the user typed (ruling 3); null keeps the flag of the
    /// pending request (a tap, a typed amount, the consent button).
    /// </param>
    private async Task HandleCoreAsync(
        ParsedRequest request, SlotAnswers tapped, CancellationToken token, LearningEntry? invalidReply = null, bool? verbatim = null)
    {
        lastRequest = request;
        if (verbatim is { } typed)
            recipientVerbatim = typed;
        answers = tapped;
        Question = null;
        QuestionMessageIndex = null;
        typedSlot = null;
        amountRetryOpen = false;

        AgentReply reply;
        await using (var scope = scopes.CreateAsyncScope())
            reply = await scope.ServiceProvider.GetRequiredService<Orchestrator>().HandleAsync(userId, request, tapped, token);

        LastReply = reply;
        ShowReply(reply);
        amountRetryOpen = OpensAmountRetry(request, reply);
        LastLearningEntry = invalidReply ?? LearningLogRule.EntryFor(reply, request);
        await WriteLearningAsync(LastLearningEntry, token);
    }

    /// <summary>The trace step of a typed amount merged into a transfer refused for an amount reason.</summary>
    private const string AmountRetryStep = "amount_retry";

    /// <summary>The refusals that leave a transfer open for one more typed amount (D6 extension, Khalil, 17 Oct 2026).</summary>
    private static readonly string[] AmountRetryCodes =
    [
        ReasonCodes.InsufficientBalance,
        ReasonCodes.OverPerTransactionCap,
        ReasonCodes.OverDailyCap,
        ReasonCodes.AmountBelowMinimum,
    ];

    /// <summary>
    /// True when <paramref name="reply"/> refuses a transfer for an amount reason (<see cref="AmountRetryCodes"/>): the
    /// next typed message, and only that one, goes into the transfer when it reads as an amount, and the checks run
    /// again. Bills and purchases carry no typed amount, so they never open it. A merged amount refused for an amount
    /// reason again opens it again (confirmed by Khalil, 17 Oct 2026).
    /// </summary>
    private static bool OpensAmountRetry(ParsedRequest request, AgentReply reply)
        => request.Intent == RequestIntent.Transfer
            && reply is AgentReply.Refusal { Code: var code }
            && AmountRetryCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Writes <paramref name="entry"/> with the masked text of the last typed message (rule 5: never the typed text).
    /// The reply is already shown, so a failed write is traced and does not change it.
    /// </summary>
    private async Task WriteLearningAsync(LearningEntry? entry, CancellationToken token)
    {
        if (suppressLearningRow)
        {
            // A message that may be a PIN is never logged (known gaps row 34), so the runner and the tests see no entry.
            LastLearningEntry = null;
            return;
        }

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

                // Ruling 3: the alias is kept only when its text is the user's own words.
                cardAlias = recipientVerbatim ? issued.Alias : null;
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

    /// <summary>The trace step of an answer the code read without the parse call; the detail is the question kind.</summary>
    private const string AnswerParsedStep = "answer_parsed";

    /// <summary>
    /// The pending request and answers a typed answer to the open question stands for, read in code before any parse
    /// call (17 Oct 2026); null when the code cannot read it, and the text then goes to the model as any message.
    /// <c>which_amount</c>: <see cref="AmountText.TryRead"/>, into the amount and its unit, answers kept.
    /// <c>which_contact</c> and <c>did_you_mean</c>, with or without options: the user's contacts are read from the
    /// wallet and matched by <see cref="ContactResolver.ResolveAnswer"/>; a card or a question puts the form that matched
    /// into the recipient, with no tapped contact (a fresh name is a fresh resolution, which the orchestrator runs again).
    /// A wallet read that fails, or no match, is null. Both read the reply without the filler words at either end
    /// (<see cref="WithoutFillerWords"/>); a reply of filler words only is null.
    /// </summary>
    private async Task<(ParsedRequest Request, SlotAnswers Answers)?> AnswerInCodeAsync(
        string kind, ParsedRequest last, string typedText, CancellationToken token)
    {
        if (WithoutFillerWords(typedText, options.CurrentValue.FillerWords) is not { } answer)
            return null;

        switch (kind)
        {
            case QuestionKinds.WhichAmount:
                return AmountText.TryRead(answer, out var value, out var unit)
                    ? (last with { AmountValue = value, AmountUnit = unit }, answers)
                    : null;

            case QuestionKinds.WhichContact:
            case QuestionKinds.DidYouMean:
                {
                    WalletResult<WalletContacts> read;
                    await using (var scope = scopes.CreateAsyncScope())
                        read = await scope.ServiceProvider.GetRequiredService<WalletClient>().GetContactsAsync(userId, token);
                    if (read is not WalletResult<WalletContacts>.Ok { Value: var contacts })
                        return null;

                    var current = options.CurrentValue;
                    var match = ContactResolver.ResolveAnswer(
                        answer, ContactCandidate.From(contacts), current.MinSubstringWordLength, current.Endearments);
                    return match.Resolution is Resolution.NotFound
                        ? null
                        : (last with { RecipientText = match.Text }, answers with { ContactOption = null });
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// The typed answer without the filler words at either end (Khalil, 17 Oct 2026, <see cref="AgentOptions.FillerWords"/>):
    /// the reply is cut into its space-separated tokens, a token whose words (<see cref="WordsOf"/>: normalised,
    /// punctuation dropped) are all filler words is a filler, and the leading run and the trailing run of fillers are
    /// dropped; a filler in the middle stays (the text then does not read in code, and goes to the model). What is left
    /// is joined by one space, its tokens as typed (so a comma between digit groups or a dot between digits is still
    /// seen by <see cref="AmountText.TryRead"/>); with no filler at either end the reply is returned unchanged. Null when
    /// nothing is left. A reply made only of D6 yes words (some filler words are yes words too) is a yes and never
    /// reaches this step; a reply with any other word in it is not a yes, so the fillers around it are dropped here.
    /// </summary>
    internal static string? WithoutFillerWords(string text, IReadOnlyCollection<string>? fillerWords)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return null;
        if (fillerWords is null || fillerWords.Count == 0)
            return text;

        var fillers = fillerWords.SelectMany(WordsOf).ToHashSet(StringComparer.Ordinal);
        bool IsFiller(string token) => WordsOf(token) is { Length: > 0 } words && words.All(fillers.Contains);

        var first = 0;
        while (first < tokens.Length && IsFiller(tokens[first]))
            first++;
        var last = tokens.Length - 1;
        while (last >= first && IsFiller(tokens[last]))
            last--;

        if (first > last)
            return null;
        return first == 0 && last == tokens.Length - 1 ? text : string.Join(' ', tokens[first..(last + 1)]);
    }

    /// <summary>
    /// A message the parse call read as unclear while a question is open (17 Oct 2026): the question is asked again and
    /// stays open, with its options. The reply is <c>not_understood</c> carrying the question's kind, with the audit row
    /// the orchestrator writes for that refusal and the learning-log entry <see cref="HandleCoreAsync"/> would write
    /// (<paramref name="invalidReply"/> for a refused structure or a refused message). The pending request, its answers
    /// and the open question are left as they were, so the next message answers the same question.
    /// </summary>
    private async Task RepeatQuestionAsync(string kind, LearningEntry? invalidReply, CancellationToken token)
    {
        trace.Record(TraceZone.Code, "question_repeated", kind);
        await using (var scope = scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuditLog>().WriteRefusalAsync(userId, ReasonCodes.NotUnderstood, null, token);

        var reply = new AgentReply.Refusal(ReasonCodes.NotUnderstood, null, null);
        LastReply = reply;
        Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.Refusal, ReasonCodes.NotUnderstood, QuestionKind: kind));
        LastLearningEntry = invalidReply ?? (lastRequest is { } pending ? LearningLogRule.EntryFor(reply, pending) : null);
        await WriteLearningAsync(LastLearningEntry, token);
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

                    // A request to pay part of a bill stays as it is, so the tapped bill gets the same refusal (ruling 2).
                    var bill = last.Intent == RequestIntent.Bill
                        || string.Equals(last.UnsupportedReason, LearningLogRule.PartOfBill, StringComparison.Ordinal)
                        ? last
                        : ParsedRequest.Bill(null, null, null, AmountUnit.None);
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
                WalletChanged(ConversationWalletChange.Payment);
                AddOutcome(OutcomeCodes.Done, done.BalanceAfterIqd, done.Reference, done.PurchaseCode);
                OfferAlias();
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
            cardAlias = null;
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

    /// <summary>
    /// Rule 18: after a Done transfer whose card carries an alias, opens the offer under the outcome and traces
    /// <c>alias_offered</c> (no detail: never the text). The card's alias is used once.
    /// </summary>
    private void OfferAlias()
    {
        if (cardAlias is not { } alias || Card is not { Type: TransferType })
            return;

        cardAlias = null;
        AliasOffer = alias;
        AliasOfferMessageIndex = Add(new ConversationMessage(ConversationRole.Agent, ConversationKind.AliasOffer));
        trace.Record(TraceZone.Code, TraceSteps.AliasOffered, null);
    }

    private void ClearAliasOffer()
    {
        AliasOffer = null;
        AliasOfferMessageIndex = null;
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
