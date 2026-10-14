using Agent.Core.Data;
using Agent.Core.Masking;

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

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;
}
