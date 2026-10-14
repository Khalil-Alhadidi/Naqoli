namespace Agent.Core.Wallet;

/// <summary>The answer of a wallet read or quote: the value, or the wallet's refusal (any 4xx, a 404 included).</summary>
public abstract record WalletResult<T>
{
    private WalletResult()
    {
    }

    public sealed record Ok(T Value) : WalletResult<T>;

    public sealed record Refused(WalletProblem Problem) : WalletResult<T>;
}
