using Agent.Core.Wallet;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Eval.Running;

/// <summary>
/// One transaction the user paid, as the runner compares it: no purchase code. <see cref="Reference"/> is the wallet's
/// reference code, kept only to link the row to the pending record whose <c>done</c> audit row names it (the
/// double-payments measure); no report prints it.
/// </summary>
public sealed record SnapshotTransaction(
    int TransactionId,
    string Type,
    string Status,
    long AmountIqd,
    long FeeIqd,
    long TotalIqd,
    string? ToPhone,
    string? BillCode,
    string? ProductCode,
    string? Reference = null);

/// <summary>What the case changed in the wallet.</summary>
/// <param name="NewTransactions">The new transactions, in the order the wallet ran them (the external debit left out).</param>
/// <param name="CodesIssued">How many purchase codes were issued (new store orders).</param>
/// <param name="BillsNow">The amount due now of every bill whose due changed, by bill code.</param>
/// <param name="BalanceIqd">The balance after the case.</param>
public sealed record WalletDiff(
    IReadOnlyList<SnapshotTransaction> NewTransactions,
    int CodesIssued,
    IReadOnlyDictionary<string, long> BillsNow,
    long BalanceIqd);

/// <summary>
/// The user's side of the wallet at one moment: the balance, the history (up to 200 rows), the amount due per bill code,
/// and the number of store orders. Read through the agent's <see cref="WalletClient"/> (reads only) and the runner's
/// <see cref="WalletAdmin"/> (the orders count).
/// </summary>
public sealed record WalletSnapshot(
    long BalanceIqd,
    IReadOnlyList<SnapshotTransaction> History,
    IReadOnlyDictionary<string, long> BillsDue,
    int OrdersCount)
{
    private const int HistoryTake = 200;

    /// <summary>Reads the snapshot of <paramref name="userId"/>.</summary>
    /// <exception cref="InvalidOperationException">A wallet read gave no answer.</exception>
    public static async Task<WalletSnapshot> TakeAsync(
        IServiceProvider services, WalletAdmin admin, int userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(admin);

        await using var scope = services.CreateAsyncScope();
        var wallet = scope.ServiceProvider.GetRequiredService<WalletClient>();

        var balance = Ok(await wallet.GetBalanceAsync(userId, cancellationToken), "balance");
        var bills = Ok(await wallet.GetBillsAsync(userId, cancellationToken), "bills");
        var history = Ok(await wallet.GetHistoryAsync(userId, null, null, HistoryTake, cancellationToken), "history");
        var products = Ok(await wallet.GetProductsAsync(null, null, null, cancellationToken), "products");
        var orders = await admin.OrdersCountAsync(userId, cancellationToken);

        var billCodes = bills.ToDictionary(b => b.BillAccountId, b => b.Code);
        var productCodes = products.ToDictionary(p => p.ProductId, p => p.Code);
        var rows = history
            .Select(h => new SnapshotTransaction(
                h.TransactionId,
                h.Type,
                h.Status,
                h.AmountIqd,
                h.FeeIqd,
                h.TotalIqd,
                h.Type == "transfer" ? h.CounterpartyPhone : null,
                h.BillAccountId is { } bill && billCodes.TryGetValue(bill, out var billCode) ? billCode : null,
                h.ProductId is { } product && productCodes.TryGetValue(product, out var productCode) ? productCode : null,
                h.Reference))
            .ToList();

        return new WalletSnapshot(
            balance.BalanceIqd,
            rows,
            bills.ToDictionary(b => b.Code, b => b.AmountDueIqd, StringComparer.Ordinal),
            orders);
    }

    /// <summary>What changed from <paramref name="before"/> to <paramref name="after"/>, leaving out <paramref name="excluded"/> transactions.</summary>
    public static WalletDiff Diff(WalletSnapshot before, WalletSnapshot after, IReadOnlyCollection<int>? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var known = before.History.Select(t => t.TransactionId).ToHashSet();
        var leftOut = excluded ?? [];
        var added = after.History
            .Where(t => !known.Contains(t.TransactionId) && !leftOut.Contains(t.TransactionId))
            .OrderBy(t => t.TransactionId)
            .ToList();

        var bills = after.BillsDue
            .Where(b => !before.BillsDue.TryGetValue(b.Key, out var was) || was != b.Value)
            .ToDictionary(b => b.Key, b => b.Value, StringComparer.Ordinal);

        return new WalletDiff(added, after.OrdersCount - before.OrdersCount, bills, after.BalanceIqd);
    }

    private static T Ok<T>(WalletResult<T> result, string what)
        => result is WalletResult<T>.Ok { Value: var value }
            ? value
            : throw new InvalidOperationException("The wallet read '" + what + "' gave no answer.");
}
