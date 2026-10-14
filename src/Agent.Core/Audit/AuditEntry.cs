namespace Agent.Core.Audit;

/// <summary>
/// One line of the agent's audit trail: an event, and the state change of a pending record if there was one. It never
/// holds a PIN, a balance or a full purchase code.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public int UserId { get; set; }

    public Guid? PendingId { get; set; }

    public string Event { get; set; } = string.Empty;

    public string? FromState { get; set; }

    public string? ToState { get; set; }

    public string? Reason { get; set; }

    public string? Reference { get; set; }

    public long? AmountIqd { get; set; }
}
