using System.Text.Json;
using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Tests.Wallet;
using Microsoft.Extensions.Configuration;

namespace Agent.Core.Tests.Resolver;

/// <summary>
/// The operator spellings of <c>Agent:ProviderSpellings</c> (Khalil's Arabic spellings, 19 Oct 2026), read from the two
/// committed <c>appsettings.json</c> files, never written here (no Arabic in C#). Pure: no database, no wallet.
/// </summary>
public class ProviderSpellingsTests
{
    /// <summary>The bill resolver's key (the internet biller), not an operator of the store.</summary>
    private const string BillerKey = "internet";

    private static string SettingsPath(string project)
        => Path.Combine(SeedHashTests.RepositoryRoot(), "src", project, "appsettings.json");

    private static string SpellingsSection(string project)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath(project)));
        return document.RootElement.GetProperty("Agent").GetProperty("ProviderSpellings").GetRawText();
    }

    /// <summary>The agent options bound from the web console's file, as the console binds them.</summary>
    private static AgentOptions ConsoleOptions()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(SettingsPath("Agent.Web"), optional: false).Build();
        var options = new AgentOptions();
        configuration.GetSection("Agent").Bind(options);
        return options;
    }

    [Fact]
    public void TheConsoleAndTheRunnerHoldTheSameSpellings()
        => Assert.Equal(SpellingsSection("Agent.Web"), SpellingsSection("Agent.Eval"));

    [Fact]
    public void EverySpellingResolvesToItsOperatorOnTheSeedCatalogue()
    {
        var options = ConsoleOptions();
        var operators = options.ProviderSpellings.Where(p => p.Key != BillerKey).ToList();
        Assert.NotEmpty(operators);

        foreach (var (provider, spellings) in operators)
        {
            Assert.NotEmpty(spellings);
            foreach (var spelling in spellings)
            {
                var resolution = ProductResolver.Resolve(ParsedRequest.Purchase(spelling, "credit", 5_000), SeedWallet.Products(), options);
                var card = Assert.IsType<ProductResolution.Card>(resolution);
                Assert.Equal(provider, card.Product.Provider, ignoreCase: true);
            }
        }
    }
}
