namespace Wallet.MockApi.Contracts;

/// <summary>A completed store purchase and its purchase code (D11).</summary>
public sealed record OrderItem(int OrderId, int TransactionId, int ProductId, string ProductName, string Code, DateTimeOffset CreatedAt);
