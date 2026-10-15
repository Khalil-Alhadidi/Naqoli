using Agent.Core.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Pipeline;

/// <summary>The kill switch (Khalil, 15 Oct 2026): in memory only, off at every start, one per application.</summary>
public class KillSwitchTests
{
    [Fact]
    public void ItIsOffAtConstruction() => Assert.False(new KillSwitch().IsOn);

    [Fact]
    public void ItFlipsBothWays()
    {
        var killSwitch = new KillSwitch();

        killSwitch.Set(true);
        Assert.True(killSwitch.IsOn);

        killSwitch.Set(false);
        Assert.False(killSwitch.IsOn);
    }

    [Fact]
    public void AddAgentCoreRegistersOneSwitchThatStartsOff()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:WalletBaseUrl"] = "http://localhost",
            ["Agent:SweepIntervalSeconds"] = "0",
        }).Build();
        using var provider = new ServiceCollection().AddAgentCore(configuration).BuildServiceProvider();

        var first = provider.GetRequiredService<KillSwitch>();
        using var scope = provider.CreateScope();

        Assert.Same(first, scope.ServiceProvider.GetRequiredService<KillSwitch>());
        Assert.False(first.IsOn);
    }
}
