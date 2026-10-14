using System.Linq.Expressions;
using Agent.Core.Audit;
using Agent.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Options;

namespace Agent.Core.Pending;

/// <summary>
/// The pending records behind the confirmation cards, and their one-way transitions (architecture section 5). Every
/// transition is one conditional update on the record's current state, so the number of rows it changed is the truth:
/// a record leaves Pending only once however many taps arrive (rule 6). Every transition writes exactly one audit row,
/// in the same database transaction, and only when the transition happened. It never calls the wallet.
/// </summary>
public sealed class PendingStore(AgentDbContext db, AuditLog audit, IOptions<AgentOptions> options, TimeProvider time)
{
    /// <summary>The most characters of a purchase code the store keeps.</summary>
    public const int PurchaseCodeDigitsKept = 4;

    /// <summary>Opens a card: a new ID, a new idempotency key, the timer from <see cref="AgentOptions.CardTimerSeconds"/>.</summary>
    public async Task<PendingRecord> CreateAsync(CardDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var now = time.GetUtcNow();
        var record = new PendingRecord
        {
            Id = Guid.NewGuid(),
            UserId = draft.UserId,
            State = PendingState.Pending,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            Type = draft.Type,
            AmountIqd = draft.AmountIqd,
            FeeIqd = draft.FeeIqd,
            TotalIqd = draft.TotalIqd,
            QuotedBalanceIqd = draft.QuotedBalanceIqd,
            ToPhone = draft.ToPhone,
            ToAccountId = draft.ToAccountId,
            DisplayName = draft.DisplayName,
            RegisteredName = draft.RegisteredName,
            BillAccountId = draft.BillAccountId,
            BillCode = draft.BillCode,
            ProductId = draft.ProductId,
            ProductCode = draft.ProductCode,
            PinRequired = draft.PinRequired,
            FirstTime = draft.FirstTime,
            Warning = draft.Warning,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(options.Value.CardTimerSeconds),
        };

        db.PendingTransactions.Add(record);
        audit.Stage(record.UserId, record.Id, AuditEvents.Created, null, nameof(PendingState.Pending), null, null, record.AmountIqd);

        // The record and its audit row are saved together.
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(record).State = EntityState.Detached;
        return record;
    }

    /// <summary>The record as stored now, or null.</summary>
    public Task<PendingRecord?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.PendingTransactions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <summary>
    /// The Confirm tap: moves the record from Pending to Executing and counts the attempt, only while its timer runs.
    /// Exactly one of any number of concurrent calls gets <see cref="TransitionResult.Begun"/>. A record found still
    /// Pending after its timer is marked Expired here.
    /// </summary>
    public async Task<TransitionResult> TryBeginExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var begun = await TransitionAsync(
            id,
            PendingState.Pending,
            PendingState.Executing,
            p => p.ExpiresAt > now,
            s => s.SetProperty(p => p.Attempts, p => p.Attempts + 1),
            AuditEvents.Executing,
            reason: null,
            reference: null,
            cancellationToken);
        if (begun is not null)
            return new TransitionResult.Begun(begun);

        var current = await FindAsync(id, cancellationToken);
        if (current is null)
            return new TransitionResult.NotFound();

        if (current.State == PendingState.Pending && current.ExpiresAt <= now)
        {
            if (await ExpireAsync(id, now, reason: null, cancellationToken))
                return new TransitionResult.Expired();

            // Another call moved it meanwhile; report where it is now.
            current = await FindAsync(id, cancellationToken) ?? current;
        }

        return new TransitionResult.AlreadyLeft(current.State);
    }

    /// <summary>The Cancel tap: Pending to Cancelled. False when the record had already left Pending.</summary>
    public async Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var cancelled = await TransitionAsync(
            id,
            PendingState.Pending,
            PendingState.Cancelled,
            where: null,
            s => s.SetProperty(p => p.ClosedAt, now),
            AuditEvents.Cancelled,
            reason: null,
            reference: null,
            cancellationToken);
        return cancelled is not null;
    }

    /// <summary>Marks Expired every Pending record whose timer has ended; returns how many it marked.</summary>
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var ids = await db.PendingTransactions.AsNoTracking()
            .Where(p => p.State == PendingState.Pending && p.ExpiresAt <= now)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var id in ids)
        {
            if (await ExpireAsync(id, now, reason: null, cancellationToken))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Settles a record that is Executing or Checking as <see cref="PendingState.Done"/> or
    /// <see cref="PendingState.NotDone"/>. Keeps at most the last four characters of a purchase code, never a whole one.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="final"/> is not Done or NotDone, or <paramref name="purchaseCodeLast4"/> is longer than four characters.</exception>
    /// <exception cref="InvalidOperationException">The record is missing, or is not Executing or Checking.</exception>
    public async Task CompleteAsync(
        Guid id,
        PendingState final,
        string? resultCode,
        string? reference,
        long? balanceAfterIqd,
        string? purchaseCodeLast4,
        CancellationToken cancellationToken)
    {
        if (final is not (PendingState.Done or PendingState.NotDone))
            throw new ArgumentException($"A record completes as Done or NotDone, not {final}.", nameof(final));
        if (purchaseCodeLast4 is { Length: > PurchaseCodeDigitsKept })
            throw new ArgumentException("Only the last four characters of a purchase code may be stored.", nameof(purchaseCodeLast4));

        var current = await FindAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Pending record {id} does not exist.");
        if (!PendingRecord.CanLeave(current.State, final))
            throw new InvalidOperationException($"Pending record {id} cannot go from {current.State} to {final}.");

        var now = time.GetUtcNow();
        var completed = await TransitionAsync(
            id,
            current.State,
            final,
            where: null,
            s => s
                .SetProperty(p => p.ResultCode, resultCode)
                .SetProperty(p => p.Reference, reference)
                .SetProperty(p => p.BalanceAfterIqd, balanceAfterIqd)
                .SetProperty(p => p.PurchaseCodeLast4, purchaseCodeLast4)
                .SetProperty(p => p.ClosedAt, now),
            final == PendingState.Done ? AuditEvents.Done : AuditEvents.NotDone,
            resultCode,
            reference,
            cancellationToken);
        if (completed is null)
            throw new InvalidOperationException($"Pending record {id} left {current.State} before it could complete.");
    }

    /// <summary>
    /// Executing back to Pending (a wrong PIN): the card stays open with the same key and the same timer.
    /// </summary>
    /// <exception cref="InvalidOperationException">The record is not Executing.</exception>
    public async Task BackToPendingAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var moved = await TransitionAsync(
            id, PendingState.Executing, PendingState.Pending, where: null, setters: null, AuditEvents.BackToPending, reason, reference: null, cancellationToken);
        if (moved is null)
            throw new InvalidOperationException($"Pending record {id} is not Executing.");
    }

    /// <summary>Executing to Checking: the wallet's answer is unknown, never shown as failed.</summary>
    /// <exception cref="InvalidOperationException">The record is not Executing.</exception>
    public async Task MarkCheckingAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var moved = await TransitionAsync(
            id, PendingState.Executing, PendingState.Checking, where: null, setters: null, AuditEvents.Checking, reason, reference: null, cancellationToken);
        if (moved is null)
            throw new InvalidOperationException($"Pending record {id} is not Executing.");
    }

    /// <summary>
    /// After a wallet reset: every Pending record becomes Expired and every Checking record becomes NotDone with
    /// <paramref name="reason"/> as its result code. Executing records are left to their executor. Returns how many moved.
    /// </summary>
    public async Task<int> ExpireAllOpenAsync(string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var now = time.GetUtcNow();
        var open = await db.PendingTransactions.AsNoTracking()
            .Where(p => p.State == PendingState.Pending || p.State == PendingState.Checking)
            .Select(p => new { p.Id, p.State })
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var record in open)
        {
            var moved = record.State == PendingState.Pending
                ? await ExpireAsync(record.Id, now, reason, cancellationToken, requireTimerEnded: false)
                : await TransitionAsync(
                    record.Id,
                    PendingState.Checking,
                    PendingState.NotDone,
                    where: null,
                    s => s.SetProperty(p => p.ResultCode, reason).SetProperty(p => p.ClosedAt, now),
                    AuditEvents.NotDone,
                    reason,
                    reference: null,
                    cancellationToken) is not null;
            if (moved)
                count++;
        }

        return count;
    }

    private async Task<bool> ExpireAsync(
        Guid id, DateTimeOffset now, string? reason, CancellationToken cancellationToken, bool requireTimerEnded = true)
    {
        var expired = await TransitionAsync(
            id,
            PendingState.Pending,
            PendingState.Expired,
            requireTimerEnded ? p => p.ExpiresAt <= now : null,
            s => s.SetProperty(p => p.ClosedAt, now),
            AuditEvents.Expired,
            reason,
            reference: null,
            cancellationToken);
        return expired is not null;
    }

    /// <summary>
    /// One conditional update from <paramref name="from"/> to <paramref name="to"/> and, only when it changed the row,
    /// its audit row, in one database transaction. Returns the record as updated, or null when no row changed.
    /// </summary>
    private async Task<PendingRecord?> TransitionAsync(
        Guid id,
        PendingState from,
        PendingState to,
        Expression<Func<PendingRecord, bool>>? where,
        Action<UpdateSettersBuilder<PendingRecord>>? setters,
        string @event,
        string? reason,
        string? reference,
        CancellationToken cancellationToken)
    {
        if (!PendingRecord.CanLeave(from, to))
            throw new InvalidOperationException($"A pending record cannot go from {from} to {to}.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var query = db.PendingTransactions.Where(p => p.Id == id && p.State == from);
        if (where is not null)
            query = query.Where(where);

        var rows = await query.ExecuteUpdateAsync(
            s =>
            {
                s.SetProperty(p => p.State, to);
                setters?.Invoke(s);
            },
            cancellationToken);
        if (rows == 0)
            return null; // Disposing the transaction rolls it back; no audit row for a transition that did not happen.

        var record = await FindAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Pending record {id} vanished during its transition.");
        await audit.WriteAsync(record.UserId, id, @event, from.ToString(), to.ToString(), reason, reference, record.AmountIqd, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return record;
    }
}
