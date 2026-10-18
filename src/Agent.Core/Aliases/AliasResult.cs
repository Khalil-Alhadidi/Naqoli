using Agent.Core.Wallet;

namespace Agent.Core.Aliases;

/// <summary>The outcome of an <see cref="AliasDirectory"/> save or delete (milestone 9a). None of them throws.</summary>
public abstract record AliasResult
{
    private AliasResult()
    {
    }

    /// <summary>The wallet saved the alias; it points to exactly one account.</summary>
    public sealed record Saved(WalletAlias Alias) : AliasResult;

    /// <summary>The wallet deleted the alias.</summary>
    public sealed record Deleted : AliasResult;

    /// <summary>
    /// Nothing written. <see cref="Code"/> is <see cref="Pipeline.ReasonCodes.AliasExists"/>,
    /// <see cref="Pipeline.ReasonCodes.AliasInvalid"/>, <see cref="Pipeline.ReasonCodes.NumberHasNoWallet"/>,
    /// <see cref="Pipeline.ReasonCodes.InvalidNumber"/>, <see cref="Pipeline.ReasonCodes.NotSupported"/> (the user's own
    /// number), <see cref="Pipeline.ReasonCodes.AccountClosed"/> (the target account is closed, review R2) or
    /// <see cref="Pipeline.ReasonCodes.RecipientNotFound"/> (the account is not among the user's contacts or aliases, or
    /// the alias id is not the user's).
    /// </summary>
    public sealed record Refused(string Code) : AliasResult;

    /// <summary>A wallet read or write gave no answer to use; nothing is known to be written.</summary>
    public sealed record Unavailable : AliasResult;
}
