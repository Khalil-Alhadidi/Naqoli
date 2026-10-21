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

        // The console's own section, validated as AddAgentWebServices registers it (the data annotations).
        services.AddOptions<ConsoleOptions>()
            .Bind(configuration.GetSection(ConsoleOptions.SectionName))
            .ValidateDataAnnotations();
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
    [InlineData("Console:WalletAdminKey")]
    [InlineData("Console:")]
    [InlineData("Agent:GridPageSize")]
    [InlineData("Console:PerTransactionCapIqd")]
    public void AKeyThatIsNotEditableIsRefused(string key)
    {
        Assert.Throws<ArgumentException>(() => Settings.TrySet(key, "1", out _));
        Assert.Throws<ArgumentException>(() => Settings.Reset(key));
        Assert.Empty(Settings.Overrides);
    }

    /// <summary>
    /// Milestone 9c (ruling 11): the most requests of a message that are prepared is editable within its range, 1 to 20;
    /// the conversation reads it at the next split.
    /// </summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("20", true)]
    [InlineData("0", false)]
    [InlineData("21", false)]
    public void MaxRequestsPerMessageIsEditableWithinItsRange(string value, bool accepted)
    {
        const string key = "Agent:" + nameof(AgentOptions.MaxRequestsPerMessage);
        Assert.Contains(nameof(AgentOptions.MaxRequestsPerMessage), RuntimeSettings.EditableNames);

        Assert.Equal(accepted, Settings.TrySet(key, value, out var error));

        Assert.Equal(accepted, error is null);
        var current = provider.GetRequiredService<IOptionsMonitor<AgentOptions>>().CurrentValue.MaxRequestsPerMessage;
        Assert.Equal(accepted ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 3, current);
    }

    /// <summary>
    /// Review R1 of milestone 9c (Khalil, 20 Oct 2026): the most parts the "not prepared" reply names is editable within
    /// its range, 1 to 20.
    /// </summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("20", true)]
    [InlineData("0", false)]
    [InlineData("21", false)]
    public void MaxNamedNotPreparedIsEditableWithinItsRange(string value, bool accepted)
    {
        const string key = "Agent:" + nameof(AgentOptions.MaxNamedNotPrepared);
        Assert.Contains(nameof(AgentOptions.MaxNamedNotPrepared), RuntimeSettings.EditableNames);

        Assert.Equal(accepted, Settings.TrySet(key, value, out var error));

        Assert.Equal(accepted, error is null);
        var current = provider.GetRequiredService<IOptionsMonitor<AgentOptions>>().CurrentValue.MaxNamedNotPrepared;
        Assert.Equal(accepted ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 5, current);
    }

    /// <summary>Security review M2: the keys the runtime store accepts are exactly the editable names.</summary>
    [Fact]
    public void EveryEditableNameCanBeSaved()
    {
        // Milestone 8: the automatic settle's interval is read live by the card panel.
        Assert.Contains(nameof(AgentOptions.CheckingRetrySeconds), RuntimeSettings.EditableNames);

        // Known gaps row 36 (Khalil, 19 Oct 2026): the longest typed message is editable.
        Assert.Contains(nameof(AgentOptions.MaxMessageChars), RuntimeSettings.EditableNames);
        var current = provider.GetRequiredService<IOptionsMonitor<AgentOptions>>().CurrentValue;
        foreach (var name in RuntimeSettings.EditableNames)
        {
            // Saving the value in force is always valid.
            var value = typeof(AgentOptions).GetProperty(name)!.GetValue(current)!.ToString()!;
            Assert.True(Settings.TrySet("Agent:" + name, value, out var error), name + ": " + error);
        }

        var console = provider.GetRequiredService<IOptionsMonitor<ConsoleOptions>>().CurrentValue;
        foreach (var name in RuntimeSettings.EditableConsoleNames)
        {
            var value = typeof(ConsoleOptions).GetProperty(name)!.GetValue(console)!.ToString()!;
            Assert.True(Settings.TrySet("Console:" + name, value, out var error), name + ": " + error);
        }
    }

    /// <summary>
    /// UI pass, task 4c (decision 2): the console grids' page size is editable within its range, 5 to 100; a refused value
    /// names the setting and the value in force stays; Reset brings back the file's 10.
    /// </summary>
    [Theory]
    [InlineData("5", true)]
    [InlineData("100", true)]
    [InlineData("4", false)]
    [InlineData("101", false)]
    [InlineData("ten", false)]
    public void GridPageSizeIsEditableWithinItsRange(string value, bool accepted)
    {
        const string key = "Console:" + nameof(ConsoleOptions.GridPageSize);
        Assert.Equal([nameof(ConsoleOptions.GridPageSize)], RuntimeSettings.EditableConsoleNames);
        var monitor = provider.GetRequiredService<IOptionsMonitor<ConsoleOptions>>();

        Assert.Equal(accepted, Settings.TrySet(key, value, out var error));

        if (accepted)
        {
            Assert.Null(error);
            Assert.Equal(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture), monitor.CurrentValue.GridPageSize);
            Settings.Reset(key);
        }
        else
        {
            Assert.NotNull(error);
            Assert.Contains(key, error, StringComparison.Ordinal);
            Assert.Empty(Settings.Overrides);
        }

        Assert.Equal(10, monitor.CurrentValue.GridPageSize);
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

    /// <summary>
    /// Review M4: a save that breaks the order minimum, per-transaction cap, daily cap is refused with the rule's
    /// message, and the value in force stays.
    /// </summary>
    [Fact]
    public void ASaveThatBreaksTheOrderOfTheLimitsIsRefused()
    {
        // The daily cap is 10,000,000 by default: a per-transaction cap above it breaks the order.
        Assert.False(Settings.TrySet(Cap, "20000000", out var error));

        Assert.NotNull(error);
        Assert.Contains("PerDayCapIqd", error, StringComparison.Ordinal);
        Assert.Contains("MinAmountIqd", error, StringComparison.Ordinal);
        Assert.Empty(Settings.Overrides);
        Assert.Equal(5_000_000, CapInANewScope());
    }
}
