namespace Wallet.MockApi.Contracts;

/// <summary>A store catalogue item (D11): the provider code, the type, the face value and the price.</summary>
public sealed record ProductItem(int ProductId, string Provider, string Type, long FaceValueIqd, long PriceIqd);
