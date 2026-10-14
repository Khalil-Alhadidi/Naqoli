namespace Agent.Core.Pending;

/// <summary>
/// Where a pending payment stands. A record leaves <see cref="Pending"/> only once (rule 6). <see cref="Checking"/>
/// means the wallet's answer is unknown; it is never shown as failed. Stored as its name.
/// </summary>
public enum PendingState
{
    Pending,
    Cancelled,
    Expired,
    Executing,
    Done,
    NotDone,
    Checking,
}
