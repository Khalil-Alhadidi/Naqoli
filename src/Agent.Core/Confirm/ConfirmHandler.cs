using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Text;
using Agent.Core.Wallet;
using Microsoft.Extensions.Options;

namespace Agent.Core.Confirm;

/// <summary>
/// The Confirm tap (rule 1: no tap, no money). Given one pending ID, it moves the record from Pending to Executing once,
/// rechecks the balance and the daily cap, and sends the one execute call under the record's idempotency key. An
/// unknown outcome becomes Checking, never failed, and <see cref="SettleCheckingAsync"/> resends the same body under the
/// same key (rule 6). <see cref="ConfirmAsync"/> and <see cref="SettleCheckingAsync"/> are the only methods in the
/// application that call <see cref="WalletClient.ExecuteAsync"/> (<c>NoTapNoMoneyTests</c>), so neither may hand that
/// call to a helper.
/// </summary>
/// <remarks>
/// The PIN travels only in the execute body: it is never stored, logged or written to an audit row. Once the wallet has
/// answered, the outcome is recorded with <see cref="CancellationToken.None"/>, so a caller who leaves cannot strand a
/// record in Executing.
/// </remarks>
public sealed class ConfirmHandler
{
    /// <summary>The reason stored when the caller cancelled a Confirm before or during the execute call.</summary>
    public const string CallerCancelled = "caller_cancelled";

    /// <summary>
    /// The result code and reply reason when the wallet locked the PIN. Provisional: not in the reason codes of
    /// <c>data/README.md</c>; reported to Khalil.
    /// </summary>
    public const string PinLocked = "pin_locked";

    private const string DoneStatus = "done";
    private const string TransferType = "transfer";

    private readonly WalletClient wallet;
    private readonly PendingStore store;
    private readonly AgentOptions options;
    private readonly TimeProvider clock;

    public ConfirmHandler(WalletClient wallet, PendingStore store, IOptions<AgentOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        this.wallet = wallet;
        this.store = store;
        this.options = options.Value;
        this.clock = clock;
    }

    /// <summary>
    /// The Confirm tap on <paramref name="pendingId"/>, with the PIN typed on the card if any (Arabic-Indic digits are
    /// accepted and sent as ASCII). Only the caller that moves the record out of Pending reaches the wallet.
    /// </summary>
    public async Task<ConfirmOutcome> ConfirmAsync(Guid pendingId, string? pin, CancellationToken cancellationToken)
    {
        PendingRecord record;
        switch (await store.TryBeginExecuteAsync(pendingId, cancellationToken))
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

        // Section 4: recheck the balance (and the daily cap), then execute. Nothing has been sent yet.
        RecheckStop? stop;
        try
        {
            stop = await RecheckAsync(record, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Nothing was sent, so the card stays open for another tap.
            await store.BackToPendingAsync(record.Id, CallerCancelled, CancellationToken.None);
            throw;
        }

        if (stop is not null)
        {
            await store.CompleteAsync(record.Id, PendingState.NotDone, stop.ResultCode, null, null, null, CancellationToken.None);
            return new ConfirmOutcome.NotDone(stop.Reason, stop.Detail);
        }

        ExecuteOutcome executed;
        try
        {
            executed = await wallet.ExecuteAsync(record.IdempotencyKey, RequestFrom(record, ToAsciiPin(pin)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The request may have reached the wallet: the outcome is unknown.
            await store.MarkCheckingAsync(record.Id, CallerCancelled, CancellationToken.None);
            throw;
        }

        switch (executed)
        {
            case ExecuteOutcome.Done done:
                return await CompleteDoneAsync(record.Id, done.Transaction);

            case ExecuteOutcome.Unknown unknown:
                await store.MarkCheckingAsync(record.Id, unknown.Reason, CancellationToken.None);
                return new ConfirmOutcome.Checking();

            case ExecuteOutcome.Refused { Problem: var problem }:
                switch (problem.Code)
                {
                    case WalletCodes.PinRequired:
                        await store.BackToPendingAsync(record.Id, problem.Code, CancellationToken.None);
                        return new ConfirmOutcome.PinRequired();

                    case WalletCodes.PinInvalid when problem.AttemptsLeft is > 0:
                        await store.BackToPendingAsync(record.Id, problem.Code, CancellationToken.None);
                        return new ConfirmOutcome.PinWrong(problem.AttemptsLeft.Value);

                    case WalletCodes.PinInvalid:
                        // The wrong PIN took the last attempt: the wallet has locked the PIN, so the card ends here.
                        await store.CompleteAsync(record.Id, PendingState.NotDone, PinLocked, null, null, null, CancellationToken.None);
                        return new ConfirmOutcome.NotDone(PinLocked, null);

                    case WalletCodes.PinMalformed:
                        await store.BackToPendingAsync(record.Id, problem.Code, CancellationToken.None);
                        return new ConfirmOutcome.PinMalformed();

                    default:
                        return await CompleteRefusedAsync(record.Id, problem);
                }

            default:
                throw new InvalidOperationException("Unknown execute outcome.");
        }
    }

    /// <summary>
    /// Settles a Checking record by resending the same body under the same key: the wallet executes it once or replays
    /// the stored outcome. Done and a refusal are final; another unknown outcome leaves the record Checking. No PIN is
    /// sent: a replay needs none, and a payment the wallet never received that needs one is refused with nothing taken.
    /// </summary>
    public async Task<ConfirmOutcome> SettleCheckingAsync(Guid pendingId, CancellationToken cancellationToken)
    {
        var record = await store.FindAsync(pendingId, cancellationToken);
        if (record is null)
            return new ConfirmOutcome.NotFound();
        if (record.State != PendingState.Checking)
            return new ConfirmOutcome.AlreadyHandled(record.State);

        var executed = await wallet.ExecuteAsync(record.IdempotencyKey, RequestFrom(record, pin: null), cancellationToken);
        if (executed is ExecuteOutcome.Unknown)
            return new ConfirmOutcome.Checking();

        // A wallet reset may have closed the record while the resend was out.
        var current = await store.FindAsync(pendingId, CancellationToken.None);
        if (current is null)
            return new ConfirmOutcome.NotFound();
        if (current.State != PendingState.Checking)
            return new ConfirmOutcome.AlreadyHandled(current.State);

        return executed switch
        {
            ExecuteOutcome.Done done => await CompleteDoneAsync(record.Id, done.Transaction),
            ExecuteOutcome.Refused refused => await CompleteRefusedAsync(record.Id, refused.Problem),
            _ => throw new InvalidOperationException("Unknown execute outcome."),
        };
    }

    /// <summary>
    /// The execute body, rebuilt from the record, so every call under its key carries the same request fingerprint.
    /// A transfer names the recipient by the account the quote resolved (the phone only when no account is stored);
    /// bills and purchases send no amount, since the wallet takes the amount due or the price.
    /// </summary>
    private static WalletPaymentRequest RequestFrom(PendingRecord record, string? pin)
    {
        var isTransfer = string.Equals(record.Type, TransferType, StringComparison.Ordinal);
        return new WalletPaymentRequest(
            record.UserId,
            record.Type,
            isTransfer ? record.AmountIqd : null,
            record.ToAccountId is null ? record.ToPhone : null,
            record.ToAccountId,
            record.BillAccountId,
            record.ProductId,
            pin);
    }

    private static string? ToAsciiPin(string? pin) => pin is null ? null : ArabicDigits.ToAscii(pin);

    private async Task<ConfirmOutcome> CompleteDoneAsync(Guid id, WalletTransaction transaction)
    {
        var last4 = transaction.Code is { Length: > 0 } code
            ? code[^Math.Min(PendingStore.PurchaseCodeDigitsKept, code.Length)..]
            : null;
        await store.CompleteAsync(
            id, PendingState.Done, null, transaction.Reference, transaction.BalanceAfterIqd, last4, CancellationToken.None);
        return new ConfirmOutcome.Done(transaction.Reference, transaction.BalanceAfterIqd, transaction.Code);
    }

    private async Task<ConfirmOutcome> CompleteRefusedAsync(Guid id, WalletProblem problem)
    {
        var (reason, detail) = MapRefusal(problem.Code);
        await store.CompleteAsync(id, PendingState.NotDone, problem.Code, null, null, null, CancellationToken.None);
        return new ConfirmOutcome.NotDone(reason, detail);
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

        var since = AgentClock.StartOfIraqDay(clock.GetUtcNow());
        switch (await wallet.GetHistoryAsync(record.UserId, since, toPhone: null, options.HistoryPageSize, cancellationToken))
        {
            case WalletResult<IReadOnlyList<WalletHistoryItem>>.Ok { Value: var items }:
                var spent = items
                    .Where(h => string.Equals(h.Status, DoneStatus, StringComparison.OrdinalIgnoreCase))
                    .Sum(h => h.AmountIqd);
                return spent + record.AmountIqd > options.PerDayCapIqd
                    ? new RecheckStop(ReasonCodes.OverDailyCap, null, ReasonCodes.OverDailyCap)
                    : null;
            case WalletResult<IReadOnlyList<WalletHistoryItem>>.Unavailable unavailable:
                return NothingTaken(ReasonCodes.WalletDetailPrefix + unavailable.Reason);
            case WalletResult<IReadOnlyList<WalletHistoryItem>>.Refused refused:
                return NothingTaken(refused.Problem.Code);
            default:
                throw new InvalidOperationException("Unknown wallet result.");
        }
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
