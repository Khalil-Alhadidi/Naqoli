using Agent.Core.Data;
using Agent.Core.Masking;
using Microsoft.EntityFrameworkCore;

namespace Agent.Core.Learning;

/// <summary>
/// Writes the learning log (D12): one row per entry <see cref="LearningLogRule"/> (or the caller of
/// <c>model_invalid_reply</c>) returns. It stores the masked text of the message, never the typed text, and has no
/// parameter for a PIN, a balance or a purchase code. The detail and the text are masked once more here, so a number
/// that reached them some other way (a typed answer to a question goes to the orchestrator as typed) never reaches a
/// row, and both are cut to their columns. A run of four to six digits (a PIN typed into the chat) is not masked; see
/// <see cref="LearningEntryRow"/>.
/// </summary>
public sealed class LearningLog(AgentDbContext db, TimeProvider time)
{
    /// <summary>Adds one row for <paramref name="entry"/>, stamped with the current time, and saves it.</summary>
    public async Task WriteAsync(int userId, LearningEntry entry, string maskedText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(maskedText);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Reason);

        db.LearningEntries.Add(new LearningEntryRow
        {
            At = time.GetUtcNow(),
            UserId = userId,
            Reason = Cut(entry.Reason, AgentDbContext.TextLength),
            Detail = entry.Detail is null ? null : Cut(Masker.Mask(entry.Detail).Text, AgentDbContext.TextLength),
            MaskedText = Cut(Masker.Mask(maskedText).Text, AgentDbContext.LearningTextLength),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The newest <paramref name="take"/> rows, newest first (by time, then by id), read without tracking.</summary>
    public async Task<IReadOnlyList<LearningEntryRow>> ReadLatestAsync(int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        return await db.LearningEntries.AsNoTracking()
            .OrderByDescending(r => r.At)
            .ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Sets the reviewed tick of the row <paramref name="id"/> and saves it; <see langword="false"/> when no row has that id.
    /// </summary>
    public async Task<bool> SetReviewedAsync(long id, bool reviewed, CancellationToken cancellationToken)
    {
        // One UPDATE: a row removed by a reset in between is a plain false, never a concurrency exception (review N2).
        var rows = await db.LearningEntries
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Reviewed, reviewed), cancellationToken);
        return rows > 0;
    }

    /// <summary>The number of rows not yet marked reviewed.</summary>
    public Task<int> CountUnreviewedAsync(CancellationToken cancellationToken) =>
        db.LearningEntries.CountAsync(r => !r.Reviewed, cancellationToken);

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;
}
