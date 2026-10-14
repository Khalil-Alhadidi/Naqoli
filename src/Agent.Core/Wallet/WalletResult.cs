namespace Agent.Core.Wallet;

/// <summary>
/// The answer of a wallet read, quote or consent call, in one of three cases: <see cref="Ok"/>, <see cref="Refused"/>
/// or <see cref="Unavailable"/>. None of them throws; only the caller's own cancellation does.
/// </summary>
public abstract record WalletResult<T>
{
    private WalletResult()
    {
    }

    /// <summary>A 2xx with a readable body.</summary>
    public sealed record Ok(T Value) : WalletResult<T>;

    /// <summary>Any 4xx, a 404 included: the wallet's own answer, with its <c>code</c>.</summary>
    public sealed record Refused(WalletProblem Problem) : WalletResult<T>;

    /// <summary>
    /// No answer to use: another status (<c>http_500</c>, <c>http_504</c>), the client's timeout (<c>timeout</c>), a
    /// transport failure (<c>connection</c>) or a 2xx body that cannot be read (<c>unreadable_response</c>). The same
    /// reasons as <see cref="ExecuteOutcome.Unknown"/>.
    /// </summary>
    public sealed record Unavailable(string Reason) : WalletResult<T>;
}
