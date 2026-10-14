namespace Wallet.MockApi.Contracts;

/// <summary>
/// A store catalogue item (D11): the product code, the lower-case provider code and the provider's names, the other
/// name the product is known by (if any), the type, the face value and the price.
/// </summary>
public sealed record ProductItem(
    int ProductId,
    string Code,
    string Provider,
    string ProviderNameEn,
    string ProviderNameAr,
    string? AlsoCalled,
    string Type,
    long FaceValueIqd,
    long PriceIqd);
