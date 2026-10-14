namespace Wallet.MockApi.Contracts;

/// <summary>
/// The wallet account behind a phone number: its id, the registered name (null when the wallet has none), and its
/// status, <c>active</c> or <c>closed</c>.
/// </summary>
public sealed record AccountSummary(int AccountId, string Phone, string? RegisteredName, string Status);
