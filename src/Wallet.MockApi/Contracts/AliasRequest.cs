namespace Wallet.MockApi.Contracts;

/// <summary>
/// The body of <c>POST /users/{id}/aliases</c> (milestone 9a): save <see cref="Text"/> as the user's alias of the one
/// account <see cref="TargetAccountId"/> (D15). The wallet stores the text trimmed.
/// </summary>
public sealed record AliasRequest(string Text, int TargetAccountId);
