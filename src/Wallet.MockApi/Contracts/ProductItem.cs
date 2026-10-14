namespace Wallet.MockApi.Contracts;

/// <summary>A store catalogue item (D11). The face value is zero for internet cards.</summary>
public sealed record ProductItem(int ProductId, string Provider, string Type, string Name, long FaceValueIqd, long PriceIqd);
