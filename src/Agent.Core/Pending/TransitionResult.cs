namespace Agent.Core.Pending;

/// <summary>
/// What a transition found: <see cref="PendingStore.TryBeginExecuteAsync"/> moves a record to Executing at most once, and
/// <see cref="PendingStore.CompleteAsync"/> settles an Executing or Checking record at most once.
/// </summary>
public abstract record TransitionResult
{
    private TransitionResult()
    {
    }

    /// <summary>This call moved the record from Pending to Executing; only this caller may execute it.</summary>
    public sealed record Begun(PendingRecord Record) : TransitionResult;

    /// <summary>This call settled the record as Done or NotDone.</summary>
    public sealed record Completed(PendingRecord Record) : TransitionResult;

    /// <summary>The card's timer had ended; the record is now Expired and nothing is executed.</summary>
    public sealed record Expired : TransitionResult;

    /// <summary>
    /// The record was not in the state the transition starts from (a double tap, a cancel, an earlier expiry, a settle
    /// that lost the race); nothing is done. <see cref="State"/> is where it is now.
    /// </summary>
    public sealed record AlreadyLeft(PendingState State) : TransitionResult;

    /// <summary>No record has this ID for this user.</summary>
    public sealed record NotFound : TransitionResult;
}
