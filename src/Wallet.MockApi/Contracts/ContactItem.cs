namespace Wallet.MockApi.Contracts;

/// <summary>
/// A saved contact of a user, resolved to the wallet account behind the phone (with its status, <c>active</c> or
/// <c>closed</c>), with the last completed transfer to them (D3).
/// </summary>
public sealed record ContactItem(
    int ContactId,
    string Code,
    string SavedName,
    string Phone,
    string? RegisteredName,
    string AccountStatus,
    int TargetAccountId,
    DateTimeOffset? LastSentAt,
    long? LastSentAmountIqd);
