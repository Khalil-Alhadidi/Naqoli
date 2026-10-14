using Agent.Core.Data;

namespace Agent.Core.Audit;

/// <summary>
/// Writes the agent's audit trail. There is no parameter for a PIN, a balance figure or a purchase code, so none can be
/// logged; <see cref="AuditEntry.AmountIqd"/> is the payment's amount, never a balance.
/// </summary>
public sealed class AuditLog(AgentDbContext db, TimeProvider time)
{
    /// <summary>Adds one entry, stamped with the current time, and saves it (with anything else the context holds).</summary>
    public async Task WriteAsync(
        int userId,
        Guid? pendingId,
        string @event,
        string? fromState,
        string? toState,
        string? reason,
        string? reference,
        long? amountIqd,
        CancellationToken cancellationToken)
    {
        Stage(userId, pendingId, @event, fromState, toState, reason, reference, amountIqd);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Adds one entry to the context without saving, so the caller saves it with its own change.</summary>
    internal void Stage(
        int userId,
        Guid? pendingId,
        string @event,
        string? fromState,
        string? toState,
        string? reason,
        string? reference,
        long? amountIqd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@event);

        db.AuditEntries.Add(new AuditEntry
        {
            At = time.GetUtcNow(),
            UserId = userId,
            PendingId = pendingId,
            Event = @event,
            FromState = fromState,
            ToState = toState,
            Reason = reason,
            Reference = reference,
            AmountIqd = amountIqd,
        });
    }
}
