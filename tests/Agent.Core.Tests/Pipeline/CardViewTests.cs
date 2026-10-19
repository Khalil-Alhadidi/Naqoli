using Agent.Core.Pending;
using Agent.Core.Pipeline;

namespace Agent.Core.Tests.Pipeline;

/// <summary>
/// The card's view: every figure and name comes from the pending record (rule 3), and nothing a component must not
/// hold (the idempotency key, the quoted balance, the row version) is on it.
/// </summary>
public class CardViewTests
{
    private static PendingRecord Record() => new()
    {
        Id = Guid.NewGuid(),
        UserId = 1,
        State = PendingState.Pending,
        IdempotencyKey = "0123456789abcdef0123456789abcdef",
        Type = "purchase",
        AmountIqd = 5_000,
        FeeIqd = 0,
        TotalIqd = 5_000,
        QuotedBalanceIqd = 750_000,
        ToPhone = "07700000002",
        DisplayName = "display",
        RegisteredName = "registered",
        BillCode = "BS1",
        ProductCode = "ZN-CR-5",
        ProductType = "credit",
        PinRequired = true,
        FirstTime = true,
        Warning = "repeat",
        CreatedAt = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 10, 14, 9, 0, 45, TimeSpan.Zero),
        RowVersion = [1, 2, 3],
    };

    [Fact]
    public void EveryFieldComesFromTheRecord()
    {
        var record = Record();

        var view = CardView.From(record);

        Assert.Equal(
            new CardView(
                record.Id, "purchase", 5_000, 0, 5_000, 745_000, "display", "registered", "07700000002", "BS1", "ZN-CR-5", "credit",
                PinRequired: true, FirstTime: true, Warning: "repeat", record.ExpiresAt),
            view);
    }

    [Fact]
    public void TheViewHoldsNoKeyQuotedBalanceOrRowVersion()
    {
        var names = typeof(CardView).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(nameof(PendingRecord.IdempotencyKey), names);
        Assert.DoesNotContain(nameof(PendingRecord.QuotedBalanceIqd), names);
        Assert.DoesNotContain(nameof(PendingRecord.RowVersion), names);
        Assert.DoesNotContain(nameof(PendingRecord.UserId), names);
    }
}
