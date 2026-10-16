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

    private static ServiceProvider Build(string environment, string? key)
    {
        var settings = new Dictionary<string, string?>();
        if (key is not null)
            settings[Setting] = key;
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
}
