namespace Agent.Core.Wallet;

/// <summary>
/// What one <c>POST /transactions</c> call says happened. <see cref="Done"/> and <see cref="Refused"/> are final;
/// <see cref="Unknown"/> means the money may or may not have moved, and the record goes to Checking, never to failed
/// (rule 6). The client never retries on its own.
/// </summary>
public abstract record ExecuteOutcome
{
    private ExecuteOutcome()
    {
    }

    /// <summary>201 (the payment ran now) or 200 (<paramref name="Replayed"/>: the stored outcome of the same key).</summary>
    public sealed record Done(WalletTransaction Transaction, bool Replayed) : ExecuteOutcome;

    /// <summary>
    /// Any 4xx except 409, or a 5xx whose ProblemDetails carries one of the codes that mean nothing was written
    /// (<c>injected_error</c>, <c>injected_timeout</c>; <see cref="WalletProblem.IsNothingWritten"/>): the wallet refused, and
    /// nothing moved.
    /// </summary>
    public sealed record Refused(WalletProblem Problem) : ExecuteOutcome;

    /// <summary>
    /// A bare 5xx with no wallet code (<c>http_500</c>, <c>http_502</c>), a 409 (<c>in_progress</c>: the key's payment
    /// is still executing), the client's timeout (<c>timeout</c>), a transport failure (<c>connection</c>), or a success
    /// body that could not be read (<c>unreadable_response</c>).
    /// </summary>
    public sealed record Unknown(string Reason) : ExecuteOutcome;
}
