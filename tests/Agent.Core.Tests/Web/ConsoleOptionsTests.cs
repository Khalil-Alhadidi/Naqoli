extern alias AgentWeb;

using AgentWeb::Agent.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Agent.Core.Tests.Web;

/// <summary>
/// Security review R2 of milestone 8 (applied at Khalil's request, 16 Oct 2026): outside Development the console's
/// wallet admin key must be set, not the placeholder <c>dev</c>, and at least 16 characters, or the console refuses to
/// start; the message names the setting and never a value. In Development the default <c>dev</c> stays.
/// </summary>
public class ConsoleOptionsTests
{
    private const string Setting = "Console:WalletAdminKey";

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Agent.Web";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider Build(string environment, string? key, string? gridPageSize = null)
    {
        var settings = new Dictionary<string, string?>();
        if (key is not null)
            settings[Setting] = key;
        if (gridPageSize is not null)
            settings["Console:" + nameof(ConsoleOptions.GridPageSize)] = gridPageSize;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(environment));
        services.AddAgentWebServices(configuration);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dev")]
    [InlineData("fifteen-chars-x")]
    public void OutsideDevelopmentAMissingPlaceholderOrShortKeyFailsAtStart(string? key)
    {
        Assert.Equal(15, "fifteen-chars-x".Length);
        using var provider = Build(Environments.Production, key);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ConsoleOptions>>().Value);

        Assert.Contains(Setting, ex.Message, StringComparison.Ordinal);
        if (key is not null)
            Assert.DoesNotContain(key, ex.Message.Replace(Setting, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void OutsideDevelopmentASixteenCharacterKeyPasses()
    {
        const string key = "sixteen-chars-xy";
        Assert.Equal(16, key.Length);
        using var provider = Build(Environments.Production, key);

        Assert.Equal(key, provider.GetRequiredService<IOptions<ConsoleOptions>>().Value.WalletAdminKey);
    }

    [Fact]
    public void InDevelopmentTheDefaultDevStays()
    {
        using var provider = Build(Environments.Development, key: null);

        Assert.Equal("dev", provider.GetRequiredService<IOptions<ConsoleOptions>>().Value.WalletAdminKey);
    }

    /// <summary>
    /// UI pass, task 4a: <c>Console:GridPageSize</c> is how many rows every console grid shows on one page, 5 to 100,
    /// default 10 when unset (three journeys count all of U1's aliases and orders on the first page, so it stays 10 or
    /// more). A value outside the range fails at start, naming the setting.
    /// </summary>
    [Theory]
    [InlineData("4", null)]
    [InlineData("101", null)]
    [InlineData("5", 5)]
    [InlineData("100", 100)]
    [InlineData(null, 10)]
    public void GridPageSizeIsBoundedFiveToOneHundred(string? value, int? expected)
    {
        using var provider = Build(Environments.Development, key: null, gridPageSize: value);
        var options = provider.GetRequiredService<IOptions<ConsoleOptions>>();

        if (expected is { } size)
        {
            Assert.Equal(size, options.Value.GridPageSize);
        }
        else
        {
            var ex = Assert.Throws<OptionsValidationException>(() => options.Value);
            Assert.Contains(nameof(ConsoleOptions.GridPageSize), ex.Message, StringComparison.Ordinal);
        }
    }
}
