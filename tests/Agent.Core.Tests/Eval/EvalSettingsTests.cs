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
