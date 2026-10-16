using Agent.Core.Wallet;
using Microsoft.Extensions.Options;

namespace Agent.Core.Pipeline;

/// <summary>
/// A resolved payment to check. <see cref="Type"/> is <c>transfer</c>, <c>bill</c> or <c>purchase</c>;
/// <see cref="AccountStatus"/> is the recipient account's status (<c>active</c> or <c>closed</c>) for a transfer.
/// <see cref="SkipFormat"/> is true when the amount comes from the wallet (a bill's amount due, a product's price).
/// </summary>
public sealed record CheckInput(
    int UserId,
    string Type,
    long AmountIqd,
    string? ToPhone,
    int? ToAccountId,
    string? AccountStatus,
    int? BillAccountId,
    int? ProductId,
    bool SkipFormat);

/// <summary>The outcome of <see cref="Checks.RunAsync"/>.</summary>
public abstract record CheckResult
{
    private CheckResult()
    {
    }

    /// <summary>Every check passed. <see cref="Quote"/> is the wallet's quote; <see cref="DailySpentIqd"/> what was paid today before this.</summary>
    public sealed record Pass(WalletQuote Quote, long DailySpentIqd) : CheckResult;

    /// <summary>
    /// The first check that failed. <see cref="Reason"/> is one of <see cref="ReasonCodes"/>; <see cref="AmountIqd"/> is
    /// the figure the reply shows: the minimum, the per-transaction cap, what is left of the daily cap, the shortfall,
    /// or the full amount due of a bill.
    /// </summary>
    public sealed record Fail(string Reason, string? Detail, long? AmountIqd) : CheckResult;
}

/// <summary>
/// The checks before a card, in the agreed order (<c>docs/matching-rules.md</c>, "Order of checks"): 1 format,
/// 2 recipient, 3 per-transaction cap, 4 daily cap, 5 balance on the total. The first that fails is the reason shown.
/// Unknown recipients and numbers with no wallet are refused before this, where the recipient is resolved; the quote's
/// refusals are mapped here as a safety net.
/// </summary>
public sealed class Checks
{
    public const string ClosedStatus = "closed";

    private const string DoneStatus = "done";

    /// <summary>
    /// The history types that are payments; an external debit (<c>externalDebit</c>, <c>POST /admin/debit</c>) is money
    /// taken from outside and never counts toward the daily cap.
    /// </summary>
    private static readonly HashSet<string> PaymentTypes = new(StringComparer.OrdinalIgnoreCase) { "transfer", "bill", "purchase" };

    private readonly WalletClient wallet;
    private readonly AgentOptions options;
    private readonly TimeProvider clock;

    public Checks(WalletClient wallet, IOptionsSnapshot<AgentOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        this.wallet = wallet;
        this.options = options.Value;
        this.clock = clock;
    }

    /// <remarks>
    /// The daily cap counts the amounts (without fees) of every <c>done</c> transaction the user paid since midnight in
    /// Iraq, of every type, read in one page of <see cref="AgentOptions.HistoryPageSize"/> items. A history read or a
    /// quote with no answer is <see cref="ReasonCodes.NotDoneNothingTaken"/> with detail <c>wallet_</c> and the reason.
    /// </remarks>
    public async Task<CheckResult> RunAsync(CheckInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 1. Format. Whole dinars are already settled by AmountRule; here only the minimum.
        if (!input.SkipFormat && input.AmountIqd < options.MinAmountIqd)
            return new CheckResult.Fail(ReasonCodes.AmountBelowMinimum, null, options.MinAmountIqd);

        // 2. Recipient.
        if (string.Equals(input.AccountStatus, ClosedStatus, StringComparison.OrdinalIgnoreCase))
            return new CheckResult.Fail(ReasonCodes.AccountClosed, null, null);

        // 3. Per-transaction cap.
        if (input.AmountIqd > options.PerTransactionCapIqd)
            return new CheckResult.Fail(ReasonCodes.OverPerTransactionCap, null, options.PerTransactionCapIqd);

        // 4. Daily cap.
        long spent;
        switch (await DailySpentAsync(input.UserId, cancellationToken))
        {
            case WalletResult<long>.Ok ok:
                spent = ok.Value;
                break;
            case WalletResult<long>.Unavailable unavailable:
                return NothingTaken(unavailable.Reason);
            case WalletResult<long>.Refused refused:
                return new CheckResult.Fail(ReasonCodes.NotDoneNothingTaken, refused.Problem.Code, null);
            default:
                throw new InvalidOperationException("Unknown wallet result.");
        }

        if (spent + input.AmountIqd > options.PerDayCapIqd)
            return new CheckResult.Fail(ReasonCodes.OverDailyCap, null, Math.Max(0, options.PerDayCapIqd - spent));

        // 5. Balance on the total.
        var request = new WalletPaymentRequest(
            input.UserId, input.Type, input.AmountIqd, input.ToPhone, input.ToAccountId, input.BillAccountId, input.ProductId, Pin: null);
        var quote = await wallet.QuoteAsync(request, cancellationToken);
        return quote switch
        {
            WalletResult<WalletQuote>.Ok { Value: { Sufficient: false } q }
                => new CheckResult.Fail(ReasonCodes.InsufficientBalance, null, q.TotalIqd - q.BalanceBeforeIqd),
            WalletResult<WalletQuote>.Ok ok => new CheckResult.Pass(ok.Value, spent),
            WalletResult<WalletQuote>.Refused refused => FromRefusal(refused.Problem),
            WalletResult<WalletQuote>.Unavailable unavailable => NothingTaken(unavailable.Reason),
            _ => throw new InvalidOperationException("Unknown wallet result."),
        };
    }

    /// <summary>
    /// What <paramref name="userId"/> has paid today: the amounts (without fees) of every <c>done</c> transaction since
    /// midnight in Iraq that is a payment (a transfer, a bill or a purchase; not an external debit), read in one page of <see cref="AgentOptions.HistoryPageSize"/> items. Used by
    /// the daily-cap check here and by the Confirm recheck. The history read's refusal or missing answer is passed on.
    /// </summary>
    public async Task<WalletResult<long>> DailySpentAsync(int userId, CancellationToken cancellationToken)
    {
        var since = AgentClock.StartOfIraqDay(clock.GetUtcNow());
        return await wallet.GetHistoryAsync(userId, since, toPhone: null, options.HistoryPageSize, cancellationToken) switch
        {
            WalletResult<IReadOnlyList<WalletHistoryItem>>.Ok ok => new WalletResult<long>.Ok(
                ok.Value
                    .Where(h => string.Equals(h.Status, DoneStatus, StringComparison.OrdinalIgnoreCase) && PaymentTypes.Contains(h.Type))
                    .Sum(h => h.AmountIqd)),
            WalletResult<IReadOnlyList<WalletHistoryItem>>.Unavailable unavailable => new WalletResult<long>.Unavailable(unavailable.Reason),
            WalletResult<IReadOnlyList<WalletHistoryItem>>.Refused refused => new WalletResult<long>.Refused(refused.Problem),
            _ => throw new InvalidOperationException("Unknown wallet result."),
        };
    }

    /// <summary>Maps a quote refusal; a code the agent has no reply for is <c>not_done_nothing_taken</c> with the code.</summary>
    private static CheckResult.Fail FromRefusal(WalletProblem problem) => problem.Code switch
    {
        "account_closed" => new(ReasonCodes.AccountClosed, null, null),
        "unknown_recipient" => new(ReasonCodes.RecipientNotFound, null, null),
        "same_account" => new(ReasonCodes.NotSupported, ReasonCodes.OwnNumberDetail, null),
        "nothing_due" => new(ReasonCodes.NothingDue, null, null),
        "bills_paid_in_full" => new(ReasonCodes.BillsPaidInFull, null, problem.AmountDueIqd),
        _ => new(ReasonCodes.NotDoneNothingTaken, problem.Code, null),
    };

    private static CheckResult.Fail NothingTaken(string reason)
        => new(ReasonCodes.NotDoneNothingTaken, ReasonCodes.WalletDetailPrefix + reason, null);
}
