using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Text;
using Agent.Core.Wallet;
using Microsoft.Extensions.Options;

namespace Agent.Core.Confirm;

/// <summary>
/// The Confirm tap (rule 1: no tap, no money). Given one pending ID and the user it belongs to, it moves the record
/// from Pending to Executing once, rechecks the balance and the daily cap, and sends the one execute call under the
/// record's idempotency key. An unknown outcome becomes Checking, never failed, and <see cref="SettleCheckingAsync"/>
/// resends the same body under the same key (rule 6). <see cref="ConfirmAsync"/> and <see cref="SettleCheckingAsync"/>
/// are the only methods in the application that call <see cref="WalletClient.ExecuteAsync"/> (<c>NoTapNoMoneyTests</c>),
/// so neither may hand that call to a helper.
/// </summary>
/// <remarks>
/// The PIN travels only in the execute body: it is never stored, logged or written to an audit row. Once the wallet has
/// answered, the outcome is recorded with <see cref="CancellationToken.None"/>, so a caller who leaves cannot strand a
/// record in Executing; an unexpected exception after the begin moves the record to Checking before it propagates.
/// </remarks>
public sealed class ConfirmHandler
{
    /// <summary>The reason stored when the caller cancelled a Confirm before or during the execute call.</summary>
    public const string CallerCancelled = "caller_cancelled";

    /// <summary>The reason stored when an unexpected exception left the outcome of a Confirm unknown.</summary>
    public const string ExceptionReason = "exception";

    /// <summary>
    /// The result code and reply reason when the wallet locked the PIN. Provisional: not in the reason codes of
    /// <c>data/README.md</c>; reported to Khalil.
    /// </summary>
    public const string PinLocked = "pin_locked";

    private readonly WalletClient wallet;
    private readonly PendingStore store;
    private readonly Checks checks;
    private readonly AgentOptions options;
    private readonly TimeProvider clock;

    public ConfirmHandler(WalletClient wallet, PendingStore store, Checks checks, IOptions<AgentOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        this.wallet = wallet;
        this.store = store;
        this.checks = checks;
        this.options = options.Value;
        this.clock = clock;
    }

    /// <summary>
    /// The Confirm tap of <paramref name="userId"/> on <paramref name="pendingId"/>, with the PIN typed on the card if
    /// any (Arabic-Indic digits are accepted and sent as ASCII). Only the caller that moves the record out of Pending
    /// reaches the wallet. A record of another user is <see cref="ConfirmOutcome.NotFound"/>.
    /// </summary>
    public async Task<ConfirmOutcome> ConfirmAsync(int userId, Guid pendingId, string? pin, CancellationToken cancellationToken)
    {
        PendingRecord record;
        switch (await store.TryBeginExecuteAsync(pendingId, userId, cancellationToken))
        {
            case TransitionResult.Begun begun:
                record = begun.Record;
                break;
            case TransitionResult.Expired:
                return new ConfirmOutcome.Expired();
            case TransitionResult.AlreadyLeft left:
                return new ConfirmOutcome.AlreadyHandled(left.State);
            case TransitionResult.NotFound:
                return new ConfirmOutcome.NotFound();
            default:
                throw new InvalidOperationException("Unknown transition result.");
        }

        try
        {
            // Section 4: recheck the balance (and the daily cap), then execute. Nothing has been sent yet.
            RecheckStop? stop;
            try
            {
                stop = await RecheckAsync(record, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Nothing was sent, so the card stays open for another tap.
                await store.BackToPendingAsync(record.Id, record.UserId, CallerCancelled, CancellationToken.None);
                throw;
            }

            if (stop is not null)
            {
                return await CompleteNotDoneAsync(record, stop.ResultCode, new ConfirmOutcome.NotDone(stop.Reason, stop.Detail));
            }

            ExecuteOutcome executed;
            try
            {
                executed = await wallet.ExecuteAsync(record.IdempotencyKey, RequestFrom(record, ToAsciiPin(pin)), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The request may have reached the wallet: the outcome is unknown.
                await store.MarkCheckingAsync(record.Id, record.UserId, CallerCancelled, CancellationToken.None);
                throw;
            }

            switch (executed)
            {
                case ExecuteOutcome.Done done:
                    return await CompleteDoneAsync(record, done.Transaction);

                case ExecuteOutcome.Unknown unknown:
                    await store.MarkCheckingAsync(record.Id, record.UserId, unknown.Reason, CancellationToken.None);
                    return new ConfirmOutcome.Checking();

                case ExecuteOutcome.Refused { Problem: var problem }:
                    switch (problem.Code)
                    {
                        case WalletCodes.PinRequired:
                            await store.BackToPendingAsync(record.Id, record.UserId, problem.Code, CancellationToken.None);
                            return new ConfirmOutcome.PinRequired();

                        case WalletCodes.PinInvalid when problem.AttemptsLeft is > 0:
                            await store.BackToPendingAsync(record.Id, record.UserId, problem.Code, CancellationToken.None);
                            return new ConfirmOutcome.PinWrong(problem.AttemptsLeft.Value);

                        case WalletCodes.PinInvalid:
                            // The wrong PIN took the last attempt: the wallet has locked the PIN, so the card ends here.
                            return await CompleteNotDoneAsync(record, PinLocked, new ConfirmOutcome.NotDone(PinLocked, null));

                        case WalletCodes.PinMalformed:
                            await store.BackToPendingAsync(record.Id, record.UserId, problem.Code, CancellationToken.None);
                            return new ConfirmOutcome.PinMalformed();

                        default:
                            return await CompleteRefusedAsync(record, problem);
                    }

                default:
                    throw new InvalidOperationException("Unknown execute outcome.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Rule 6: the execute call may have reached the wallet, so the record must not stay Executing. Checking is
            // settled later under the same key; the exception still reaches the caller.
            await MarkCheckingBestEffortAsync(record);
            throw;
        }
    }

    /// <summary>
    /// Settles a Checking record of <paramref name="userId"/> by resending the same body under the same key: the
    /// wallet executes it once or replays the stored outcome. Done and a stored refusal are final; another unknown
    /// outcome leaves the record Checking. No PIN is sent: a replay needs none.
    /// </summary>
    /// <remarks>
    /// The wallet stores nothing for a PIN refusal, so <c>pin_required</c>, <c>pin_invalid</c>, <c>pin_malformed</c>
    /// and <c>pin_locked</c> on a settle may only mean the original call (with its PIN) has not reached the database
    /// yet. Such a refusal leaves the record Checking while the record is younger than
    /// <see cref="AgentOptions.SettleWindowSeconds"/>; after that the original call can no longer be running, and the
    /// record ends NotDone (<see cref="ReasonCodes.NotDoneNothingTaken"/>, detail the wallet's code).
    /// </remarks>
    public async Task<ConfirmOutcome> SettleCheckingAsync(int userId, Guid pendingId, CancellationToken cancellationToken)
    {
        var record = await store.FindAsync(pendingId, userId, cancellationToken);
        if (record is null)
            return new ConfirmOutcome.NotFound();
        if (record.State != PendingState.Checking)
            return new ConfirmOutcome.AlreadyHandled(record.State);

        var executed = await wallet.ExecuteAsync(record.IdempotencyKey, RequestFrom(record, pin: null), cancellationToken);
        switch (executed)
        {
            case ExecuteOutcome.Unknown:
                return new ConfirmOutcome.Checking();

            case ExecuteOutcome.Done done:
                return await CompleteDoneAsync(record, done.Transaction);

            case ExecuteOutcome.Refused { Problem: var problem } when IsPinRefusal(problem.Code):
                if (!SettleWindowEnded(record))
                    return new ConfirmOutcome.Checking();
                return await CompleteNotDoneAsync(
                    record, problem.Code, new ConfirmOutcome.NotDone(ReasonCodes.NotDoneNothingTaken, problem.Code));

            case ExecuteOutcome.Refused refused:
                return await CompleteRefusedAsync(record, refused.Problem);

            default:
                throw new InvalidOperationException("Unknown execute outcome.");
        }
    }

    /// <summary>
    /// The execute body, rebuilt from the record, so every call under its key carries the same request fingerprint.
    /// A transfer names the recipient by the account the quote resolved (the phone only when no account is stored).
    /// Every type carries the confirmed amount, so the wallet refuses a bill or a price that no longer matches the card.
    /// </summary>
    private static WalletPaymentRequest RequestFrom(PendingRecord record, string? pin)
        => new(
            record.UserId,
            record.Type,
            record.AmountIqd,
            record.ToAccountId is null ? record.ToPhone : null,
            record.ToAccountId,
            record.BillAccountId,
            record.ProductId,
            pin);

    private static string? ToAsciiPin(string? pin) => pin is null ? null : ArabicDigits.ToAscii(pin);

    private static bool IsPinRefusal(string code)
        => code is WalletCodes.PinRequired or WalletCodes.PinInvalid or WalletCodes.PinMalformed or WalletCodes.PinLocked;

    /// <summary>True when the record is older than the settle window, so its original execute call has ended.</summary>
    private bool SettleWindowEnded(PendingRecord record)
        => clock.GetUtcNow() - record.CreatedAt > TimeSpan.FromSeconds(options.SettleWindowSeconds);

    private async Task<ConfirmOutcome> CompleteDoneAsync(PendingRecord record, WalletTransaction transaction)
    {
        var last4 = transaction.Code is { Length: > 0 } code
            ? code[^Math.Min(PendingStore.PurchaseCodeDigitsKept, code.Length)..]
            : null;
        var completed = await store.CompleteAsync(
            record.Id, record.UserId, PendingState.Done, null, transaction.Reference, transaction.BalanceAfterIqd, last4, CancellationToken.None);
        return Settled(completed, new ConfirmOutcome.Done(transaction.Reference, transaction.BalanceAfterIqd, transaction.Code));
    }

    private Task<ConfirmOutcome> CompleteRefusedAsync(PendingRecord record, WalletProblem problem)
    {
        var (reason, detail) = MapRefusal(problem.Code);
        return CompleteNotDoneAsync(record, problem.Code, new ConfirmOutcome.NotDone(reason, detail));
    }

    private async Task<ConfirmOutcome> CompleteNotDoneAsync(PendingRecord record, string resultCode, ConfirmOutcome.NotDone outcome)
    {
        var completed = await store.CompleteAsync(
            record.Id, record.UserId, PendingState.NotDone, resultCode, null, null, null, CancellationToken.None);
        return Settled(completed, outcome);
    }

    /// <summary>
    /// The outcome of a completion: <paramref name="outcome"/> when this call settled the record; otherwise another call
    /// (a concurrent settle, a reset) got there first and the record is reported where it is.
    /// </summary>
    private static ConfirmOutcome Settled(TransitionResult completed, ConfirmOutcome outcome) => completed switch
    {
        TransitionResult.Completed => outcome,
        TransitionResult.AlreadyLeft left => new ConfirmOutcome.AlreadyHandled(left.State),
        TransitionResult.NotFound => new ConfirmOutcome.NotFound(),
        _ => throw new InvalidOperationException("Unknown transition result."),
    };

    /// <summary>
    /// Moves the record to Checking after an unexpected exception. Best effort: if this fails too (the record already
    /// left Executing, or the database is unreachable), the original exception still propagates and the stale-Executing
    /// sweep moves the record later.
    /// </summary>
    private async Task MarkCheckingBestEffortAsync(PendingRecord record)
    {
        try
        {
            await store.MarkCheckingAsync(record.Id, record.UserId, ExceptionReason, CancellationToken.None);
        }
        catch (Exception)
        {
            // Deliberately ignored: the caller sees the original exception.
        }
    }

    /// <summary>
    /// The reply for a final wallet refusal. Every code the agent has no reply of its own for is
    /// <see cref="ReasonCodes.NotDoneNothingTaken"/> with the wallet's code as the detail: a 4xx moved nothing.
    /// </summary>
    private static (string Reason, string? Detail) MapRefusal(string code) => code switch
    {
        WalletCodes.InsufficientFunds => (ReasonCodes.InsufficientBalance, null),
        WalletCodes.NothingDue => (ReasonCodes.NothingDue, null),
        WalletCodes.AccountClosed => (ReasonCodes.AccountClosed, null),
        WalletCodes.PinLocked => (PinLocked, null),
        _ => (ReasonCodes.NotDoneNothingTaken, code),
    };

    /// <summary>The recheck before the execute: the balance on the total, then the daily cap. Null when both pass.</summary>
    private async Task<RecheckStop?> RecheckAsync(PendingRecord record, CancellationToken cancellationToken)
    {
        switch (await wallet.GetBalanceAsync(record.UserId, cancellationToken))
        {
            case WalletResult<WalletBalance>.Ok { Value: var balance } when balance.BalanceIqd < record.TotalIqd:
                return new RecheckStop(ReasonCodes.InsufficientBalance, null, ReasonCodes.InsufficientBalance);
            case WalletResult<WalletBalance>.Ok:
                break;
            case WalletResult<WalletBalance>.Unavailable unavailable:
                return NothingTaken(ReasonCodes.WalletDetailPrefix + unavailable.Reason);
            case WalletResult<WalletBalance>.Refused refused:
                return NothingTaken(refused.Problem.Code);
            default:
                throw new InvalidOperationException("Unknown wallet result.");
        }

        return await checks.DailySpentAsync(record.UserId, cancellationToken) switch
        {
            WalletResult<long>.Ok { Value: var spent } when spent + record.AmountIqd > options.PerDayCapIqd
                => new RecheckStop(ReasonCodes.OverDailyCap, null, ReasonCodes.OverDailyCap),
            WalletResult<long>.Ok => null,
            WalletResult<long>.Unavailable unavailable => NothingTaken(ReasonCodes.WalletDetailPrefix + unavailable.Reason),
            WalletResult<long>.Refused refused => NothingTaken(refused.Problem.Code),
            _ => throw new InvalidOperationException("Unknown wallet result."),
        };
    }

    private static RecheckStop NothingTaken(string detail) => new(ReasonCodes.NotDoneNothingTaken, detail, detail);

    /// <summary>Why the recheck stopped the payment: the reply's reason and detail, and the record's result code.</summary>
    private sealed record RecheckStop(string Reason, string? Detail, string ResultCode);

    /// <summary>The wallet's refusal codes the handler acts on (<c>docs/wallet-api.md</c>, Error codes).</summary>
    private static class WalletCodes
    {
        public const string PinRequired = "pin_required";
        public const string PinInvalid = "pin_invalid";
        public const string PinMalformed = "pin_malformed";
        public const string PinLocked = "pin_locked";
        public const string InsufficientFunds = "insufficient_funds";
        public const string NothingDue = "nothing_due";
        public const string AccountClosed = "account_closed";
    }
}
