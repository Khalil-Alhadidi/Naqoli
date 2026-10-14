namespace Agent.Core.Pending;

/// <summary>What a Confirm tap found: <see cref="PendingStore.TryBeginExecuteAsync"/> moves a record to Executing at most once.</summary>
public abstract record TransitionResult
{
    private TransitionResult()
    {
    }

    /// <summary>This call moved the record from Pending to Executing; only this caller may execute it.</summary>
    public sealed record Begun(PendingRecord Record) : TransitionResult;

    /// <summary>The card's timer had ended; the record is now Expired and nothing is executed.</summary>
    public sealed record Expired : TransitionResult;

    /// <summary>The record had already left Pending (a double tap, a cancel, an earlier expiry); nothing is done.</summary>
    public sealed record AlreadyLeft(PendingState State) : TransitionResult;

    /// <summary>No record has this ID.</summary>
    public sealed record NotFound : TransitionResult;
}
