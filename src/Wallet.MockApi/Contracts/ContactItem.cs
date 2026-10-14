namespace Wallet.MockApi.Contracts;

/// <summary>A saved contact of a user, resolved to the wallet holder behind the phone, with the last completed transfer to them (D3).</summary>
public sealed record ContactItem(
    int ContactId,
    string SavedName,
    string Phone,
    string RegisteredName,
    int TargetAccountId,
    DateTimeOffset? LastSentAt,
    long? LastSentAmountIqd);
