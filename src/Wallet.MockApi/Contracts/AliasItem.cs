namespace Wallet.MockApi.Contracts;

/// <summary>An alias of a user and the one account it points to (D15), with that account's status.</summary>
public sealed record AliasItem(
    int AliasId,
    string Text,
    int TargetAccountId,
    string Phone,
    string? RegisteredName,
    string AccountStatus);
