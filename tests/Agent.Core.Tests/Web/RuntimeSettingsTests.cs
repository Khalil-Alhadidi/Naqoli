extern alias AgentWeb;

using Agent.Core.Tests.Agent;
using AgentWeb::Agent.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agent.Core.Tests.Web;

/// <summary>
/// D8: an agent limit saved in the settings component applies to the next action without a restart. The runtime source
/// is the last configuration source, so a saved value wins over the file's; each action's scope reads
/// <see cref="IOptionsSnapshot{TOptions}"/>, recomputed per scope. No database or wallet is touched.
/// </summary>
public sealed class RuntimeSettingsTests : IDisposable
{
    private const string Cap = "Agent:PerTransactionCapIqd";

    private readonly ServiceProvider provider;
    private readonly RuntimeSettingsProvider runtimeProvider;

    public RuntimeSettingsTests()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:WalletBaseUrl"] = "http://localhost",
                ["Agent:WalletClientKey"] = AgentHost.TestWalletClientKey,
                ["Agent:SweepIntervalSeconds"] = "0",
                [Cap] = "5000000",
                // Present for AddAgentCore; nothing connects.
                ["ConnectionStrings:Agent"] = AgentHost.DefaultConnectionString,
            })
            .AddRuntimeSettings()
            .Build();

        runtimeProvider = configuration.Providers.OfType<RuntimeSettingsProvider>().Single();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentCore(configuration);
        services.AddRuntimeSettings(configuration);
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public void Dispose() => provider.Dispose();

    private RuntimeSettings Settings => provider.GetRequiredService<RuntimeSettings>();

    private long CapInANewScope()
    {
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<AgentOptions>>().Value.PerTransactionCapIqd;
    }

    private long CapOfTheMonitor => provider.GetRequiredService<IOptionsMonitor<AgentOptions>>().CurrentValue.PerTransactionCapIqd;

    [Fact]
    public void ASavedValueAppliesToTheNextScope()
    {
        Assert.Equal(5_000_000, CapInANewScope());

        Assert.True(Settings.TrySet(Cap, "40000", out var error), error);

        Assert.Null(error);
        Assert.Equal(40_000, CapInANewScope());
        Assert.Equal(40_000, CapOfTheMonitor);
        Assert.Equal("40000", Settings.Overrides[Cap]);
    }

    [Fact]
    public void AScopeOpenBeforeTheSaveKeepsItsValue()
    {
        using var scope = provider.CreateScope();
        var before = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<AgentOptions>>().Value.PerTransactionCapIqd;

        Assert.True(Settings.TrySet(Cap, "40000", out _));

        Assert.Equal(5_000_000, before);
        Assert.Equal(5_000_000, scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<AgentOptions>>().Value.PerTransactionCapIqd);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("forty")]
    public void AnInvalidValueIsRefusedAndTheOldOneStays(string value)
    {
        Assert.True(Settings.TrySet(Cap, "40000", out _));

        Assert.False(Settings.TrySet(Cap, value, out var error));

        Assert.NotNull(error);
        Assert.Contains("PerTransactionCapIqd", error, StringComparison.Ordinal);
        Assert.Equal(40_000, CapInANewScope());
        Assert.Equal(40_000, CapOfTheMonitor);
        Assert.Equal("40000", Settings.Overrides[Cap]);

        // The monitor still follows later saves.
        Assert.True(Settings.TrySet(Cap, "60000", out _));
        Assert.Equal(60_000, CapOfTheMonitor);
    }

    [Fact]
    public void AnInvalidFirstValueLeavesNoOverride()
    {
        Assert.False(Settings.TrySet(Cap, "0", out _));

        Assert.Empty(Settings.Overrides);
        Assert.Equal(5_000_000, CapInANewScope());
    }

    [Fact]
    public void ResetRestoresTheFileValue()
    {
        Assert.True(Settings.TrySet(Cap, "40000", out _));

        Settings.Reset(Cap);

        Assert.Empty(Settings.Overrides);
        Assert.Equal(5_000_000, CapInANewScope());
        Assert.Equal(5_000_000, CapOfTheMonitor);
    }

    [Theory]
    [InlineData("Model:Candidate")]
    [InlineData("ConnectionStrings:Agent")]
    [InlineData("Anthropic:ApiKey")]
    [InlineData("AgentX:PerTransactionCapIqd")]
    [InlineData("Agent:")]
    [InlineData("Agent:WalletClientKey")]
    [InlineData("Agent:WalletBaseUrl")]
    [InlineData("Agent:DevSetPath")]
    [InlineData("Agent:Parser")]
    [InlineData("Agent:DemoUserId")]
    [InlineData("Agent:MaxRequestsPerMessage")]
    public void AKeyThatIsNotEditableIsRefused(string key)
    {
        Assert.Throws<ArgumentException>(() => Settings.TrySet(key, "1", out _));
        Assert.Throws<ArgumentException>(() => Settings.Reset(key));
        Assert.Empty(Settings.Overrides);
    }

    /// <summary>Security review M2: the keys the runtime store accepts are exactly the editable names.</summary>
    [Fact]
    public void EveryEditableNameCanBeSaved()
    {
        var current = provider.GetRequiredService<IOptionsMonitor<AgentOptions>>().CurrentValue;
        foreach (var name in RuntimeSettings.EditableNames)
        {
            // Saving the value in force is always valid.
            var value = typeof(AgentOptions).GetProperty(name)!.GetValue(current)!.ToString()!;
            Assert.True(Settings.TrySet("Agent:" + name, value, out var error), name + ": " + error);
        }
    }

    /// <summary>
    /// Security review M1: a refused value is checked on a candidate and never published: the provider's data never
    /// holds it and its reload token never fires, so no other circuit can build options from it.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("forty")]
    public void AnInvalidSaveNeverReachesTheProvider(string value)
    {
        var reloads = 0;
        using var subscription = Microsoft.Extensions.Primitives.ChangeToken.OnChange(runtimeProvider.GetReloadToken, () => reloads++);

        Assert.False(Settings.TrySet(Cap, value, out var error));

        Assert.NotNull(error);
        Assert.False(runtimeProvider.TryGet(Cap, out _));
        Assert.Equal(0, reloads);
        Assert.Equal(5_000_000, CapOfTheMonitor);
    }
}
