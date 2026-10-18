extern alias AgentWeb;

using System.Globalization;
using System.Xml.Linq;
using Agent.Eval.Running;
using AgentWeb::Agent.Web.Services;

namespace Agent.Core.Tests.Web;

/// <summary>The phone's resource file (<c>src/Agent.Web/Resources/PhoneStrings.resx</c>): Khalil's wording, complete and well formed.</summary>
public class PhoneStringsTests
{
    private const string Placeholder = "TODO-AR";

    private static Dictionary<string, string> Values()
    {
        var path = Path.Combine(EvalPaths.RepositoryRoot(), "src", "Agent.Web", "Resources", "PhoneStrings.resx");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => d.Element("value")!.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Keys added after Khalil wrote the phone's wording and still English placeholders until he writes them: empty since
    /// 17 Oct 2026; a new key goes here until its Arabic is in, and no other key may be a placeholder.
    /// </summary>
    /// <summary>The keys of milestone 9a (the alias offer and the phone's directory), awaiting Khalil's Arabic.</summary>
    private static readonly string[] AwaitingKhalil =
    [
        "Directory_Back",
        "Directory_Delete",
        "Directory_Empty",
        "Directory_Open",
        "Directory_Phone",
        "Directory_Save",
        "Directory_Target",
        "Directory_TargetNone",
        "Directory_Text",
        "Directory_Title",
        "Offer_save_alias",
        "Offer_save_alias_button",
        "Refusal_alias_exists",
        "Refusal_alias_invalid",
        "Refusal_alias_shared",
        "Reply_alias_deleted",
        "Reply_alias_saved",
    ];

    [Fact]
    public void NoValueIsAPlaceholderExceptTheKeysAwaitingKhalil()
    {
        var placeholders = Values()
            .Where(v => v.Value.StartsWith(Placeholder, StringComparison.Ordinal))
            .Select(v => v.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(AwaitingKhalil, placeholders);
    }

    [Fact]
    public void NoValueIsBlankOrHasAnEdgeSpace()
    {
        foreach (var (key, value) in Values())
        {
            Assert.False(string.IsNullOrWhiteSpace(value), key + " is blank");
            Assert.Equal(value.Trim(), value);
        }
    }

    [Fact]
    public void EveryValueFormatsWithTwoArguments()
    {
        // A stray brace would throw at the moment a reply is built; braces here are {0} and {1} only.
        foreach (var (key, value) in Values())
        {
            var formatted = string.Format(CultureInfo.InvariantCulture, value, "a", "b");
            Assert.DoesNotContain('{', formatted);
            Assert.DoesNotContain('}', formatted);
        }
    }

    [Fact]
    public void TheYesAndNoListsSplitIntoWordsThatDoNotOverlap()
    {
        var values = Values();
        var yes = PhoneWords.Split(values["Word_yes"]);
        var no = PhoneWords.Split(values["Word_no"]);
        Assert.NotEmpty(yes);
        Assert.NotEmpty(no);
        Assert.All(yes.Concat(no), w => Assert.Equal(w.Trim(), w));
        Assert.Empty(yes.Intersect(no, StringComparer.OrdinalIgnoreCase));
    }
}
