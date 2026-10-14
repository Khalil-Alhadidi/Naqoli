using Agent.Core.Pending;

namespace Agent.Core.Pipeline;

/// <summary>
/// What a confirmation card shows, projected from its pending record by code (rule 3). It is the only shape of a card a
/// component receives: it carries no idempotency key, no quoted balance, no row version and no user ID.
/// <see cref="BalanceAfterIqd"/> is the quoted balance less the total.
/// </summary>
public sealed record CardView(
    Guid PendingId,
    string Type,
    long AmountIqd,
    long FeeIqd,
    long TotalIqd,
    long BalanceAfterIqd,
    string? DisplayName,
    string? RegisteredName,
    string? ToPhone,
    string? BillCode,
    string? ProductCode,
    bool PinRequired,
    bool FirstTime,
    string? Warning,
    DateTimeOffset ExpiresAt)
{
    public static CardView From(PendingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new CardView(
            record.Id,
            record.Type,
            record.AmountIqd,
            record.FeeIqd,
            record.TotalIqd,
            record.QuotedBalanceIqd - record.TotalIqd,
            record.DisplayName,
            record.RegisteredName,
            record.ToPhone,
            record.BillCode,
            record.ProductCode,
            record.PinRequired,
            record.FirstTime,
            record.Warning,
            record.ExpiresAt);
    }
}
