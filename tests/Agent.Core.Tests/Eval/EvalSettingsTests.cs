extern alias AgentWeb;

using System.Text.Json;
using System.Xml.Linq;
using Agent.Core;
using Agent.Eval.Running;
using AgentWeb::Agent.Web.Services;

namespace Agent.Core.Tests.Eval;

/// <summary>The runner's committed settings (<c>src/Agent.Eval/appsettings.json</c>) against the web console's.</summary>
public class EvalSettingsTests
{
    private static string Root => EvalPaths.RepositoryRoot();

    private static JsonElement EvalAgentSection()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Agent.Eval", "appsettings.json")));
        return document.RootElement.GetProperty("Agent").Clone();
    }

    private static string Resource(string name)
    {
        var resx = XDocument.Load(Path.Combine(Root, "src", "Agent.Web", "Resources", "PhoneStrings.resx"));
        return resx.Root!.Elements("data").Single(d => (string?)d.Attribute("name") == name).Element("value")!.Value;
    }

    [Fact]
    public void TheD6WordsMatchThePhoneResourceFile()
    {
        var agent = EvalAgentSection();

        // The resource holds each list as one comma-separated string; the runner holds the split words.
        Assert.Equal(PhoneWords.Split(Resource("Word_yes")), agent.GetProperty("YesWords").EnumerateArray().Select(e => e.GetString()!));
        Assert.Equal(PhoneWords.Split(Resource("Word_no")), agent.GetProperty("NoWords").EnumerateArray().Select(e => e.GetString()!));
    }

    [Fact]
    public void TheAgentLimitsMatchTheWebConsole()
    {
        var agent = EvalAgentSection();
        using var web = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Agent.Web", "appsettings.json")));

        foreach (var setting in web.RootElement.GetProperty("Agent").EnumerateObject().Where(p => p.Name != "SweepIntervalSeconds"))
            Assert.Equal(setting.Value.GetRawText(), agent.GetProperty(setting.Name).GetRawText());
        Assert.Equal(0, agent.GetProperty("SweepIntervalSeconds").GetInt32());
    }

    [Fact]
    public void TheModelCandidatesMatchTheWebConsole()
    {
        // The candidates, their prices (the cache prices included) and the prompt cache lifetime: the runner prices and
        // calls the model exactly as the console does.
        using var eval = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Agent.Eval", "appsettings.json")));
        using var web = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Agent.Web", "appsettings.json")));
        var evalCandidates = eval.RootElement.GetProperty("Model").GetProperty("Candidates");
        var webCandidates = web.RootElement.GetProperty("Model").GetProperty("Candidates");

        Assert.Equal(
            webCandidates.EnumerateObject().Select(c => c.Name),
            evalCandidates.EnumerateObject().Select(c => c.Name));
        foreach (var candidate in webCandidates.EnumerateObject())
        {
            Assert.True(candidate.Value.TryGetProperty("CacheWritePricePerMTok", out _), candidate.Name);
            Assert.True(candidate.Value.TryGetProperty("CacheReadPricePerMTok", out _), candidate.Name);
            Assert.True(candidate.Value.TryGetProperty("PromptCacheTtl", out _), candidate.Name);
            foreach (var setting in candidate.Value.EnumerateObject())
                Assert.Equal(setting.Value.GetRawText(), evalCandidates.GetProperty(candidate.Name).GetProperty(setting.Name).GetRawText());
        }
    }

    [Theory]
    [InlineData(0, 0, 0, "n/a")]
    [InlineData(1_000, 0, 0, "0.0 %")]
    [InlineData(200, 1_000, 3_000, "71.4 %")]
    [InlineData(0, 0, 5_000, "100.0 %")]
    [InlineData(1, 0, 2, "66.7 %")]
    public void TheCacheHitRateIsTheReadsShareOfAllInput(long input, long writes, long reads, string expected)
        => Assert.Equal(expected, EvalCommand.CacheHitRate(input, writes, reads));

    [Fact]
    public void TheReportHeaderShowsKeysOnlyAsSetAndWordsOnlyAsACount()
    {
        var agent = new AgentOptions
        {
            WalletBaseUrl = new Uri("http://localhost:5276"),
            WalletClientKey = "a-client-key-of-16+",
            YesWords = ["yes", "ok"],
            ProviderSpellings = new(StringComparer.Ordinal) { ["internet"] = ["one", "two", "three"] },
        };

        var settings = EvalCommand.Settings(new EvalOptions { WalletAdminKey = "an-admin-key" }, agent);

        Assert.Equal("set", settings["Eval:WalletAdminKey"]);
        Assert.Equal("set", settings["Agent:WalletClientKey"]);
        Assert.Equal("2 word(s)", settings["Agent:YesWords"]);
        Assert.Equal("internet 3 spelling(s)", settings["Agent:ProviderSpellings"]);
        Assert.Equal("12000", settings["Eval:FaultDelayMs"]);
        Assert.Equal("http://localhost:5276", settings["Agent:WalletBaseUrl"]);
        Assert.DoesNotContain(settings.Values, v => v.Contains("key", StringComparison.Ordinal));
    }
}
