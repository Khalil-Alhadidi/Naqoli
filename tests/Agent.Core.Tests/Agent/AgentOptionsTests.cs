using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Agent;

public class AgentOptionsTests
{
    [Fact]
    public void DefaultsMatchTheSettingsFile()
    {
        var options = new AgentOptions();
        var s = SeedFacts.Seed.Settings;

        Assert.Equal(s.MinAmount, options.MinAmountIqd);
        Assert.Equal(s.Caps.PerTransaction, options.PerTransactionCapIqd);
        Assert.Equal(s.Caps.PerDay, options.PerDayCapIqd);
        Assert.Equal(s.CardTimerSeconds, options.CardTimerSeconds);
        Assert.Equal(s.MaxRequestsPerMessage, options.MaxRequestsPerMessage);
        Assert.Equal(s.MaxProductsInOneQuestion, options.MaxProductsInOneQuestion);
        Assert.Equal(s.RepeatWarningWindowMinutes, options.RepeatWarningWindowMinutes);
        Assert.Equal(s.HistoryDays, options.HistoryDays);
    }

    [Theory]
    [InlineData("2026-10-14T22:30:00+00:00", "2026-10-15T00:00:00+03:00")]
    [InlineData("2026-10-14T20:59:59+00:00", "2026-10-14T00:00:00+03:00")]
    [InlineData("2026-10-15T00:00:00+03:00", "2026-10-15T00:00:00+03:00")]
    public void StartOfIraqDayIsMidnightInIraq(string now, string expected)
    {
        var start = AgentClock.StartOfIraqDay(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), start);
        Assert.Equal(AgentClock.IraqOffset, start.Offset);
    }
}
