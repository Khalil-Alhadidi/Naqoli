namespace Wallet.MockApi.Contracts;

/// <summary>A wallet holder: id, phone and the registered name.</summary>
public sealed record UserSummary(int Id, string Phone, string RegisteredName);
