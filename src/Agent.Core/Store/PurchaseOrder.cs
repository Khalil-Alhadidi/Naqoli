using System.Globalization;

namespace Agent.Core.Store;

/// <summary>
/// One of the user's store purchases for the My orders screen (milestone 9b, ruling 5): the wallet's order joined with
/// the catalogue. The product fields are null when the catalogue no longer has the product. <see cref="Code"/> is shown
/// on the phone only; it never goes to the model, the trace or a log.
/// </summary>
public sealed record PurchaseOrder(
    int OrderId,
    string Code,
    DateTimeOffset CreatedAt,
    string? ProviderNameAr,
    string? ProductType,
    long? PriceIqd)
{
    /// <summary>Keeps the purchase code out of any string form of the order.</summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"PurchaseOrder {{ OrderId = {OrderId}, Code = ***, CreatedAt = {CreatedAt:O}, ProviderNameAr = {ProviderNameAr}, ProductType = {ProductType}, PriceIqd = {PriceIqd} }}");
}
