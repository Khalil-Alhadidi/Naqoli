using Agent.Core.Tests.Wallet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    [Fact]
    public void MilestoneFourSettingsHaveTheirDefaults()
    {
        var options = new AgentOptions();

        Assert.Equal(1, options.DemoUserId);
        Assert.Equal(5, options.SweepIntervalSeconds);
        Assert.Equal(40, options.StaleExecutingAfterSeconds);
        Assert.Equal(3, options.MinSubstringWordLength);
    }

    [Theory]
    [InlineData("40", "10", true)]
    [InlineData("30", "10", false)]
    [InlineData("31", "10", true)]
    public void StaleExecutingMustOutlastThreeWalletCalls(string staleAfter, string walletTimeout, bool valid)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:WalletBaseUrl"] = "http://localhost",
                ["Agent:WalletClientKey"] = AgentHost.TestWalletClientKey,
                ["Agent:StaleExecutingAfterSeconds"] = staleAfter,
                ["Agent:WalletTimeoutSeconds"] = walletTimeout,
            })
            .Build();
        using var provider = new ServiceCollection().AddAgentCore(configuration).BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<AgentOptions>>().Value;

        if (valid)
            Assert.NotNull(read());
        else
            Assert.Throws<OptionsValidationException>(read);
    }

    /// <summary>Review M4 (Khalil, 16 Oct 2026): the minimum, the per-transaction cap and the daily cap are in that order.</summary>
    [Theory]
    [InlineData("1000", "5000000", "10000000", true)]
    [InlineData("5000000", "5000000", "5000000", true)]
    [InlineData("6000", "5000", "10000000", false)]
    [InlineData("1000", "20000000", "10000000", false)]
    public void TheThreeLimitsMustBeOrdered(string minimum, string perTransaction, string perDay, bool valid)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:WalletBaseUrl"] = "http://localhost",
                ["Agent:WalletClientKey"] = AgentHost.TestWalletClientKey,
                ["Agent:MinAmountIqd"] = minimum,
                ["Agent:PerTransactionCapIqd"] = perTransaction,
                ["Agent:PerDayCapIqd"] = perDay,
            })
            .Build();
        using var provider = new ServiceCollection().AddAgentCore(configuration).BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<AgentOptions>>().Value;

        if (valid)
        {
            Assert.NotNull(read());
            return;
        }

        var error = Assert.Throws<OptionsValidationException>(read);
        Assert.Contains("MinAmountIqd", error.Message, StringComparison.Ordinal);
        Assert.Contains("PerTransactionCapIqd", error.Message, StringComparison.Ordinal);
        Assert.Contains("PerDayCapIqd", error.Message, StringComparison.Ordinal);
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
