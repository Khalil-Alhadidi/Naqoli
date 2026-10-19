using System.Globalization;
using Agent.Core.Trace;
using Agent.Core.Wallet;

namespace Agent.Core.Store;

/// <summary>
/// Reads the user's store purchases for the My orders screen (milestone 9b, ruling 5): the catalogue and the orders from
/// the wallet, joined by product id, in the wallet's order (newest first). Moves no money and writes nothing. The trace
/// records the count only, never a code.
/// </summary>
public sealed class OrdersReader
{
    private readonly WalletClient wallet;
    private readonly ITraceSink trace;

    public OrdersReader(WalletClient wallet, ITraceSink trace)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(trace);

        this.wallet = wallet;
        this.trace = trace;
    }

    /// <summary>
    /// The orders of <paramref name="userId"/>, newest first, each with its product's operator name, type and price when
    /// the catalogue still has it. The first wallet read that gives no usable answer is returned as it came (its
    /// <see cref="WalletResult{T}.Refused"/> problem or <see cref="WalletResult{T}.Unavailable"/> reason), and nothing is
    /// traced.
    /// </summary>
    public async Task<WalletResult<IReadOnlyList<PurchaseOrder>>> ListAsync(int userId, CancellationToken cancellationToken)
    {
        var catalogueRead = await wallet.GetProductsAsync(null, null, null, cancellationToken);
        if (catalogueRead is not WalletResult<IReadOnlyList<WalletProduct>>.Ok { Value: var catalogue })
            return Failed(catalogueRead);

        var ordersRead = await wallet.GetOrdersAsync(userId, cancellationToken);
        if (ordersRead is not WalletResult<IReadOnlyList<WalletOrder>>.Ok { Value: var orders })
            return Failed(ordersRead);

        var products = catalogue
            .GroupBy(p => p.ProductId)
            .ToDictionary(g => g.Key, g => g.First());
        var joined = orders
            .Select(o => products.TryGetValue(o.ProductId, out var p)
                ? new PurchaseOrder(o.OrderId, o.Code, o.CreatedAt, p.ProviderNameAr, p.Type, p.PriceIqd)
                : new PurchaseOrder(o.OrderId, o.Code, o.CreatedAt, null, null, null))
            .ToList();

        trace.Record(TraceZone.Code, TraceSteps.OrdersRead, joined.Count.ToString(CultureInfo.InvariantCulture));
        return new WalletResult<IReadOnlyList<PurchaseOrder>>.Ok(joined);
    }

    private static WalletResult<IReadOnlyList<PurchaseOrder>> Failed<T>(WalletResult<T> result) => result switch
    {
        WalletResult<T>.Refused refused => new WalletResult<IReadOnlyList<PurchaseOrder>>.Refused(refused.Problem),
        WalletResult<T>.Unavailable unavailable => new WalletResult<IReadOnlyList<PurchaseOrder>>.Unavailable(unavailable.Reason),
        _ => throw new InvalidOperationException("A usable wallet answer is not a failure."),
    };
}
