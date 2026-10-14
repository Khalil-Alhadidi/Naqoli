namespace Wallet.MockApi.Contracts;

/// <summary>A completed store purchase, the code of the product bought, and its purchase code (D11).</summary>
public sealed record OrderItem(
    int OrderId,
    int TransactionId,
    int ProductId,
    string ProductCode,
    string Code,
    DateTimeOffset CreatedAt);
