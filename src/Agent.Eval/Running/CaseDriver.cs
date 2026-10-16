using System.Diagnostics;
using System.Globalization;
using Agent.Core;
using Agent.Core.Conversation;
using Agent.Core.Data;
using Agent.Core.Parsing;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Text;
using Agent.Core.Wallet;
using Agent.Eval.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AgentConversation = Agent.Core.Conversation.Conversation;

namespace Agent.Eval.Running;

/// <summary>
/// Plays one case through a fresh <see cref="AgentConversation"/>, the same logic the phone runs, and records what it saw
/// (<see cref="CaseObservation"/>). It never moves money itself: a Confirm step calls the conversation's
/// <see cref="AgentConversation.ConfirmAsync"/> and a Checking outcome its <see cref="AgentConversation.SettleAsync"/>,
/// the only paths to the Confirm handler (rule 1). The external debit of fault <c>external_debit</c> is the outside
/// world's payment, made through <see cref="WalletAdmin"/>, and is left out of the case's transactions.
/// </summary>
/// <remarks>
/// Before each case: the wallet is reset to the seed (faults cleared), every open pending record is closed, the audit
/// log and the learning log are emptied, the clock is set back to the real time, and the parser's record is cleared.
/// Every exception of a step is caught and kept as <see cref="CaseObservation.StepError"/> (<see cref="StepErrorOf"/>: the
/// type and a fixed message, never the exception's message, which may echo case text), and the case stops there; the run
/// goes on with the next case.
/// </remarks>
public sealed class CaseDriver
{
    private const string WalletResetReason = "wallet_reset";

    /// <summary>The fixed message of every <see cref="CaseObservation.StepError"/> after the exception's type.</summary>
    public const string StepErrorMessage = "the step threw an exception";

    /// <summary>
    /// What a step's exception leaves in the report: its type name and <see cref="StepErrorMessage"/>, never its own
    /// message, which a model library might fill with the input (review M1).
    /// </summary>
    public static string StepErrorOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.GetType().Name + ": " + StepErrorMessage;
    }

    private readonly IServiceProvider services;
    private readonly WalletAdmin admin;
    private readonly SeedPins pins;
    private readonly EvalClock clock;
    private readonly RecordingParser parser;
    private readonly EvalOptions options;
    private readonly EvalTrace trace;
    private readonly Dictionary<string, int> userIds = new(StringComparer.Ordinal);

    public CaseDriver(
        IServiceProvider services,
        WalletAdmin admin,
        SeedPins pins,
        EvalClock clock,
        RecordingParser parser,
        EvalOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(options);

        this.services = services;
        this.admin = admin;
        this.pins = pins;
        this.clock = clock;
        this.parser = parser;
        this.options = options;
        trace = services.GetService<EvalTrace>() ?? new EvalTrace();
    }

    /// <summary>The wallet fault mode of a confirm-time fault code, or null for a code that is not one.</summary>
    public static string? WalletFaultMode(string code) => code switch
    {
        "wallet_error" => "error",
        "wallet_timeout_before_execute" => "timeoutBeforeExecute",
        "wallet_timeout_after_execute" => "timeoutAfterExecute",
        _ => null,
    };

    /// <summary>Plays <paramref name="evalCase"/> from a fresh wallet and a fresh conversation.</summary>
    public async Task<CaseObservation> PlayAsync(EvalCase evalCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evalCase);

        var stopwatch = Stopwatch.StartNew();
        await admin.ResetAsync(cancellationToken);
        await ResetAgentAsync(cancellationToken);
        clock.Reset();
        parser.Reset();
        trace.Clear();

        var userId = await UserIdAsync(evalCase.User, cancellationToken);
        var pin = pins.PinOf(evalCase.User);
        var before = await WalletSnapshot.TakeAsync(services, admin, userId, cancellationToken);

        var watcher = new CardWatcher();
        var excluded = new List<int>();
        var steps = new List<StepObservation>();
        string? unsupported = null;
        string? error = null;
        var confirmTaps = 0;
        var fault = evalCase.Fault;

        var conversation = new AgentConversation(
            services.GetRequiredService<IServiceScopeFactory>(),
            parser,
            clock,
            services.GetRequiredService<IOptions<AgentOptions>>(),
            trace,
            userId);
        conversation.Changed += () => watcher.Observe(conversation.Card, conversation.CardState);

        try
        {
            if (fault is { On: "request" } && ParserFaultOf(fault.Code) is { } parserFault)
                parser.Arm(parserFault, fault.Times ?? 1);

            var walletFaultSet = false;
            var debited = false;
            for (var index = 0; index < evalCase.Steps.Count; index++)
            {
                var step = evalCase.Steps[index];
                var kind = KindOf(step);
                var messagesBefore = conversation.Messages.Count;
                var cardBefore = conversation.Card?.PendingId;
                var statusesBefore = watcher.Sequence.Count;

                if (step.Tap is "save_alias")
                {
                    // 9a (save an alias) is not built yet.
                    unsupported = kind;
                    break;
                }

                try
                {
                    if (step.Tap == "confirm" && fault is { On: "confirm" } && !walletFaultSet && WalletFaultMode(fault.Code) is { } mode)
                    {
                        await admin.SetFaultAsync(mode, options.FaultDelayMs, once: true, cancellationToken);
                        walletFaultSet = true;
                    }

                    // A Confirm step counts once, with or without twice, as soon as it is played.
                    if (step.Tap == "confirm")
                        confirmTaps++;

                    if (!await RunWithTimeoutAsync(() => PlayStepAsync(conversation, step, pin, evalCase.User, cancellationToken), cancellationToken))
                    {
                        error = "timeout";
                        conversation.Dispose();
                    }
                    else
                    {
                        await SettleCheckingAsync(conversation, cancellationToken);

                        if (fault is { Code: "external_debit", On: "card_waiting", Amount: { } amount } && !debited
                            && conversation.CardState == CardStatus.Live)
                        {
                            debited = true;
                            var debit = await admin.ExternalDebitAsync(userId, amount, cancellationToken);
                            if (debit.TransactionId is { } debitId)
                                excluded.Add(debitId);
                            else if (debit.Status == ExternalDebitResult.Unsupported)
                                unsupported = "external_debit amount";
                            else
                                error = "external_debit failed: " + debit.Problem;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    error = StepErrorOf(ex);
                }

                if (error is null || error == "timeout")
                {
                    steps.Add(await ObserveStepAsync(
                        conversation, index, kind, messagesBefore, cardBefore, watcher, statusesBefore, before, excluded, pin, cancellationToken));
                }

                if (error is not null || unsupported is not null)
                    break;
            }
        }
        finally
        {
            conversation.Dispose();
        }

        WalletSnapshot after;
        try
        {
            after = await WalletSnapshot.TakeAsync(services, admin, userId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error ??= StepErrorOf(ex);
            after = before;
        }

        // The entry the conversation wrote for its last reply (model_invalid_reply included), not one derived again here.
        var learning = conversation.LastLearningEntry;
        var usages = parser.Usages;
        var usage = new ParseUsage(
            usages.Sum(u => u.InputTokens),
            usages.Sum(u => u.OutputTokens),
            usages.Sum(u => u.CostUsd),
            TimeSpan.FromTicks(usages.Sum(u => u.Duration.Ticks)));
        stopwatch.Stop();

        return new CaseObservation(
            evalCase.Id,
            userId,
            steps,
            watcher.Cards,
            conversation.QuestionsAsked,
            [.. conversation.Messages.Where(m => m.Role == ConversationRole.Agent)],
            conversation.ConsentShown,
            learning,
            parser.Texts,
            usage,
            stopwatch.ElapsedMilliseconds,
            before,
            after,
            WalletSnapshot.Diff(before, after, excluded),
            unsupported,
            error,
            watcher.Sequence,
            confirmTaps);
    }

    /// <summary>The step form, as the observation names it (no case text).</summary>
    public static string KindOf(CaseStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Say is not null)
            return "say";
        if (step.Wait is { } wait)
            return "wait " + wait;
        return "tap " + step.Tap;
    }

    private static ParserFault? ParserFaultOf(string code) => code switch
    {
        "model_unreachable" => ParserFault.Unreachable,
        "model_invalid_reply" => ParserFault.InvalidReply,
        _ => null,
    };

    private async Task PlayStepAsync(AgentConversation conversation, CaseStep step, string pin, string userCode, CancellationToken cancellationToken)
    {
        if (step.Say is { } text)
        {
            await conversation.SendAsync(text);
            return;
        }

        if (step.Wait is { } wait)
        {
            if (wait is "expire" or "expire_if_card")
                await WaitOutTheCardAsync(conversation, cancellationToken);
            return;
        }

        switch (step.Tap)
        {
            case "option":
                await conversation.TapOptionAsync(step.ValueText ?? string.Empty);
                break;

            case "confirm":
                var typed = step.Pin switch
                {
                    "correct" => pin,
                    "wrong" => pins.WrongPinOf(userCode),
                    _ => null,
                };
                if (step.Twice)
                    await Task.WhenAll(conversation.ConfirmAsync(typed), conversation.ConfirmAsync(typed));
                else
                    await conversation.ConfirmAsync(typed);
                break;

            case "cancel":
                await conversation.CancelAsync();
                break;

            case "try_again":
                // D19: the card's Try again (or Send again) button; a new card that waits for its own Confirm.
                await conversation.ReissueAsync();
                break;

            default:
                throw new InvalidOperationException("Unknown tap " + step.Tap + ".");
        }
    }

    /// <summary>
    /// The user does nothing until the card's timer ends: the clock moves past <c>ExpiresAt</c> by one second, the
    /// sweep expires the record, and the conversation reads it again. Nothing happens when no card is live.
    /// </summary>
    private async Task WaitOutTheCardAsync(AgentConversation conversation, CancellationToken cancellationToken)
    {
        if (conversation.Card is not { } card || conversation.CardState != CardStatus.Live)
            return;

        var wait = card.ExpiresAt - clock.Now + TimeSpan.FromSeconds(1);
        if (wait > TimeSpan.Zero)
            clock.Advance(wait);

        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PendingStore>().ExpireDueAsync(cancellationToken);

        await conversation.RefreshAsync();
    }

    /// <summary>After a Checking outcome, taps "check again" up to <see cref="EvalOptions.SettleAttempts"/> times.</summary>
    private async Task SettleCheckingAsync(AgentConversation conversation, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < options.SettleAttempts && conversation.CardState == CardStatus.Checking; attempt++)
        {
            if (options.SettleDelayMs > 0)
                await Task.Delay(options.SettleDelayMs, cancellationToken);
            if (!await RunWithTimeoutAsync(conversation.SettleAsync, cancellationToken))
                throw new TimeoutException("A settle did not finish in time.");
        }
    }

    /// <summary>Runs <paramref name="action"/>; false when it outlasts <see cref="EvalOptions.CaseTimeoutSeconds"/>.</summary>
    private async Task<bool> RunWithTimeoutAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        var running = action();
        var timer = Task.Delay(TimeSpan.FromSeconds(options.CaseTimeoutSeconds), cancellationToken);
        if (await Task.WhenAny(running, timer) != running)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        await running;
        return true;
    }

    private async Task<StepObservation> ObserveStepAsync(
        AgentConversation conversation,
        int index,
        string kind,
        int messagesBefore,
        Guid? cardBefore,
        CardWatcher watcher,
        int statusesBefore,
        WalletSnapshot before,
        IReadOnlyCollection<int> excluded,
        string pin,
        CancellationToken cancellationToken)
    {
        var replies = conversation.Messages.Skip(messagesBefore).Where(m => m.Role == ConversationRole.Agent).ToList();
        var outcome = replies.LastOrDefault(m => m.Kind == ConversationKind.Outcome);
        var attemptsLeft = replies.LastOrDefault(m => m.Code == OutcomeCodes.PinWrong)?.AttemptsLeft;
        var card = conversation.Card;
        var cardIssued = card is not null && card.PendingId != cardBefore;

        var now = await WalletSnapshot.TakeAsync(services, admin, conversation.UserId, cancellationToken);
        var diff = WalletSnapshot.Diff(before, now, excluded);
        var done = diff.NewTransactions.Where(t => t.Status == "done").ToList();

        return new StepObservation(
            index,
            kind,
            conversation.CardState is { } state ? CaseObservation.StateName(state) : null,
            outcome?.Code,
            ReplyCodes(replies),
            replies,
            done.Sum(t => t.TotalIqd),
            done.Count,
            attemptsLeft,
            cardIssued,
            cardIssued && cardBefore is not null,
            await PinSeenAsync(conversation.Messages, pin, cancellationToken),
            [.. watcher.Sequence.Skip(statusesBefore)]);
    }

    /// <summary>The reason codes among <paramref name="replies"/>, in order.</summary>
    private static List<string> ReplyCodes(IEnumerable<ConversationMessage> replies)
    {
        var codes = new List<string>();
        foreach (var reply in replies)
        {
            switch (reply.Kind)
            {
                case ConversationKind.Refusal or ConversationKind.Info or ConversationKind.Unsupported when reply.Code is { } code:
                    codes.Add(code);
                    break;
                case ConversationKind.Balance:
                    codes.Add(ReasonCodes.Balance);
                    break;
                case ConversationKind.Outcome when reply.Code == OutcomeCodes.NotDone && reply.Reason is { } reason:
                    codes.Add(reason);
                    break;
                case ConversationKind.Outcome when reply.Code == OutcomeCodes.Cancelled:
                    codes.Add(ReasonCodes.CardCancelled);
                    break;
            }
        }

        return codes;
    }

    /// <summary>
    /// True when the PIN, in either digit form, is in a message, a trace step, a text handed to the parse call, an audit
    /// row or a learning-log row.
    /// </summary>
    private async Task<bool> PinSeenAsync(IReadOnlyList<ConversationMessage> messages, string pin, CancellationToken cancellationToken)
    {
        string[] forms = [pin, ArabicDigits.ToArabicIndic(pin)];
        bool Holds(string? value) => value is not null && forms.Any(f => value.Contains(f, StringComparison.Ordinal));
        bool IsPin(long? figure) => figure?.ToString(CultureInfo.InvariantCulture) == pin;

        if (messages.Any(m => Holds(m.Code) || Holds(m.Reference) || Holds(m.PurchaseCode) || Holds(m.Reason)
            || IsPin(m.AmountIqd) || IsPin(m.AttemptsLeft)))
        {
            return true;
        }

        if (trace.Entries.Any(e => Holds(e.Step) || Holds(e.Detail)))
            return true;

        // What the parse call received (review I2): a PIN typed into the chat is not masked (four to six digits).
        if (parser.Texts.Any(Holds))
            return true;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var rows = await db.AuditEntries.AsNoTracking().ToListAsync(cancellationToken);
        if (rows.Any(r => Holds(r.Event) || Holds(r.FromState) || Holds(r.ToState) || Holds(r.Reason) || Holds(r.Reference)
            || Holds(r.Detail) || IsPin(r.AmountIqd)))
        {
            return true;
        }

        var learning = await db.LearningEntries.AsNoTracking().ToListAsync(cancellationToken);
        return learning.Any(l => Holds(l.Reason) || Holds(l.Detail) || Holds(l.MaskedText));
    }

    /// <summary>Closes every open pending record (the wallet was just reset) and empties the audit log and the learning log.</summary>
    private async Task ResetAgentAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PendingStore>().ExpireAllOpenAsync(WalletResetReason, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AgentDbContext>().AuditEntries.ExecuteDeleteAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AgentDbContext>().LearningEntries.ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>The wallet user id of a seed user code, read once through <see cref="WalletClient.GetUserAsync"/>.</summary>
    private async Task<int> UserIdAsync(string userCode, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            await using var scope = services.CreateAsyncScope();
            var wallet = scope.ServiceProvider.GetRequiredService<WalletClient>();
            for (var id = 1; id <= pins.UserCodes.Count(); id++)
            {
                if (await wallet.GetUserAsync(id, cancellationToken) is WalletResult<WalletUser>.Ok { Value: var user })
                    userIds[user.Code] = user.Id;
            }
        }

        return userIds.TryGetValue(userCode, out var userId)
            ? userId
            : throw new InvalidOperationException("The wallet has no user " + userCode + ".");
    }

    /// <summary>Follows the card through the conversation's changes: every card issued, its last state, and the states in order.</summary>
    private sealed class CardWatcher
    {
        private readonly Lock gate = new();
        private readonly List<(Guid Id, CardView View, string? State)> cards = [];
        private readonly List<string> sequence = [];
        private (Guid? Id, string? State) last;

        public IReadOnlyList<string> Sequence
        {
            get
            {
                lock (gate)
                    return [.. sequence];
            }
        }

        public IReadOnlyList<CardResult> Cards
        {
            get
            {
                lock (gate)
                    return [.. cards.Select(c => new CardResult(c.View, c.State))];
            }
        }

        public void Observe(CardView? card, CardStatus? status)
        {
            if (card is null || status is not { } known)
                return;

            var state = CaseObservation.StateName(known);
            lock (gate)
            {
                var at = cards.FindIndex(c => c.Id == card.PendingId);
                if (at < 0)
                    cards.Add((card.PendingId, card, state));
                else
                    cards[at] = (card.PendingId, card, state);

                if (last != (card.PendingId, state))
                {
                    sequence.Add(state);
                    last = (card.PendingId, state);
                }
            }
        }
    }
}
