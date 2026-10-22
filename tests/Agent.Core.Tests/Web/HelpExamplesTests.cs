using System.Xml.Linq;
using Agent.Eval.Running;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The phone's Help screen (22 Oct 2026): every example line is the <c>message</c> of a development-set case, character
/// for character, so the help never shows a request the parser is not proven on. Only <c>requests.dev.jsonl</c> is read
/// (rule 7).
/// </summary>
public class HelpExamplesTests
{
    /// <summary>Each <c>Help_Example_*</c> key and the development-set case its line is copied from.</summary>
    private static readonly (string Key, string CaseId)[] Sources =
    [
        ("Help_Example_Transfer_1", "CLN-01"),
        ("Help_Example_Transfer_2", "CLN-03"),
        ("Help_Example_Transfer_3", "ADD-01"),
        ("Help_Example_Bill_1", "ADD-05"),
        ("Help_Example_Bill_2", "CLN-23"),
        ("Help_Example_Bill_3", "CLN-25"),
        ("Help_Example_Purchase_1", "CLN-29"),
        ("Help_Example_Purchase_2", "CLN-27"),
        ("Help_Example_Balance_1", "CLN-31"),
    ];

    public static TheoryData<string, string> Examples()
    {
        var data = new TheoryData<string, string>();
        foreach (var (key, caseId) in Sources)
            data.Add(key, caseId);
        return data;
    }

    private static Dictionary<string, string> Values()
    {
        var path = Path.Combine(EvalPaths.RepositoryRoot(), "src", "Agent.Web", "Resources", "PhoneStrings.resx");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => d.Element("value")!.Value, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryExampleIsItsDevSetMessage(string key, string caseId)
    {
        var values = Values();

        Assert.True(values.ContainsKey(key), key + " is missing");
        Assert.Equal(DevSetText.Message(caseId), values[key]);
    }

    [Fact]
    public void EveryExampleKeyIsListed()
    {
        // A Help_Example_ key added to the resource file without a case here would escape the check above.
        var listed = Sources.Select(s => s.Key).Order(StringComparer.Ordinal).ToArray();
        var present = Values().Keys
            .Where(k => k.StartsWith("Help_Example_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(listed, present);
    }
}
