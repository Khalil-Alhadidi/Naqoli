using System.Globalization;
using Agent.Core.Data;
using Agent.Core.Pipeline;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Writes a <c>refused</c> row. The detail is kept only when <paramref name="reason"/> is
    /// <see cref="ReasonCodes.NotDoneNothingTaken"/>, where it is always a wallet code or <c>wallet_</c> and a reason;
    /// any other refusal's detail (which may be the user's own words) is dropped.
    /// </summary>
    public async Task WriteRefusalAsync(int userId, string reason, string? detail, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var kept = string.Equals(reason, ReasonCodes.NotDoneNothingTaken, StringComparison.Ordinal) ? detail : null;
        if (kept is { Length: > AgentDbContext.TextLength })
            kept = kept[..AgentDbContext.TextLength];

        Stage(userId, null, AuditEvents.Refused, null, null, reason, null, null, kept);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Writes an <see cref="AuditEvents.AliasSaved"/> or <see cref="AuditEvents.AliasDeleted"/> row (milestone 9a, ruling
    /// 6) whose detail is <paramref name="accountId"/> as invariant text. There is no parameter for the alias text, so the
    /// audit log never holds it; the wallet does.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="event"/> is not one of the two alias events.</exception>
    public async Task WriteAliasAsync(int userId, string @event, int accountId, CancellationToken cancellationToken)
    {
        if (@event is not (AuditEvents.AliasSaved or AuditEvents.AliasDeleted))
            throw new ArgumentException("Not an alias event: " + @event, nameof(@event));

        Stage(userId, null, @event, null, null, null, null, null, accountId.ToString(CultureInfo.InvariantCulture));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The newest <paramref name="take"/> entries, newest first (by <see cref="AuditEntry.At"/>, then by
    /// <see cref="AuditEntry.Id"/>), untracked: the console's log component reads them. The rows hold no PIN, balance
    /// figure or purchase code, so none can be shown.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="take"/> is below 1.</exception>
    public async Task<IReadOnlyList<AuditEntry>> ReadLatestAsync(int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        return await db.AuditEntries.AsNoTracking()
            .OrderByDescending(e => e.At)
            .ThenByDescending(e => e.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
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
        long? amountIqd,
        string? detail = null)
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
            Detail = detail,
        });
    }
}
