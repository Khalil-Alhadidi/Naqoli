using System.Globalization;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

/// <summary>
/// Seed dates across midnight in Iraq (UTC+3), which falls at 21:00 UTC: <see cref="SeedData.TodayIn"/>,
/// <see cref="SeedData.At"/> and the history dates <see cref="SeedData.Build"/> gives. Fixed instants; no database.
/// </summary>
public class SeedDatesTests
{
    [Theory]
    [InlineData("2026-10-14T22:30:00Z", "2026-10-15")]
    [InlineData("2026-10-14T20:59:00Z", "2026-10-14")]
    [InlineData("2026-10-14T21:00:00Z", "2026-10-15")]
    public void TodayInIsTheIraqDate(string instant, string expected)
        => Assert.Equal(
            DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            SeedData.TodayIn(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));

    [Fact]
    public void AtCountsDaysBackInIraqTime()
    {
        var today = new DateOnly(2026, 10, 15);

        Assert.Equal(new DateTimeOffset(2026, 10, 6, 19, 34, 0, TimeSpan.FromHours(3)), SeedData.At(today, 9, "19:34"));
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 16, 36, 0, TimeSpan.FromHours(3)), SeedData.At(today, 0, "16:36"));
    }

    [Fact]
    public void BuildAfterUtcEveningDatesHistoryFromTheIraqDate()
    {
        // 22:30 UTC on 14 Oct is 01:30 on 15 Oct in Iraq, so "9 days ago" is 6 Oct.
        var graph = SeedData.Build(SeedSetTests.Seed, new DateTimeOffset(2026, 10, 14, 22, 30, 0, TimeSpan.Zero));

        var h0001 = Assert.Single(graph.Transactions, t => t.IdempotencyKey == "seed-H0001");
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 19, 34, 0, TimeSpan.FromHours(3)), h0001.CreatedAt);
        Assert.All(graph.Transactions, t => Assert.Equal(SeedData.IraqOffset, t.CreatedAt.Offset));
    }
}
