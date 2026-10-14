namespace Wallet.MockApi.Contracts;

/// <summary>The balance of a user's default account at a moment (D22).</summary>
public sealed record BalanceResponse(int AccountId, long BalanceIqd, DateTimeOffset AsOf);
