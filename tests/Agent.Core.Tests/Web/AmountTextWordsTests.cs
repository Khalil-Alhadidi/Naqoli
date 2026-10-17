using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The amount reader's words (<c>src/Agent.Core/Text/amount-words.json</c>) and the card's words (the <c>Words_*</c> keys
/// of the phone's resource file) are the same values, byte for byte, so the two cannot drift.
/// </summary>
public class AmountTextWordsTests
{
    private static Dictionary<string, string> Resx()
    {
        var path = Path.Combine(SeedHashTests.RepositoryRoot(), "src", "Agent.Web", "Resources", "PhoneStrings.resx");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => d.Element("value")!.Value, StringComparer.Ordinal);
    }

    private static JsonDocument Words()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(SeedHashTests.RepositoryRoot(), "src", "Agent.Core", "Text", "amount-words.json")));

    [Fact]
    public void EveryNumberWordEqualsTheResourceValueOfTheSameNumber()
    {
        var resx = Resx();
        using var words = Words();
        var root = words.RootElement;

        int[] numbers =
        [
            .. Enumerable.Range(1, 19),
            .. Enumerable.Range(2, 8).Select(i => i * 10),
            .. Enumerable.Range(1, 9).Select(i => i * 100),
        ];
        foreach (var number in numbers)
        {
            var key = number.ToString(CultureInfo.InvariantCulture);
            Assert.Equal(resx["Words_" + key], root.GetProperty(key).GetString());
        }

        var constructs = root.GetProperty("construct");
        Assert.Equal(8, constructs.EnumerateObject().Count());
        for (var i = 3; i <= 10; i++)
        {
            var key = i.ToString(CultureInfo.InvariantCulture);
            Assert.Equal(resx["Words_Construct_" + key], constructs.GetProperty(key).GetString());
        }

        // The first word of each scale list is the card's word; a list may add Khalil's other spellings after it.
        Assert.Equal(resx["Words_Thousand"], root.GetProperty("thousand")[0].GetString());
        Assert.Equal(resx["Words_TwoThousand"], root.GetProperty("two_thousand")[0].GetString());
        Assert.Equal(resx["Words_Thousands"], root.GetProperty("thousands")[0].GetString());
        Assert.Equal(resx["Words_Million"], root.GetProperty("million")[0].GetString());
        Assert.Equal(resx["Words_TwoMillion"], root.GetProperty("two_million")[0].GetString());
        Assert.Equal(resx["Words_Millions"], root.GetProperty("millions")[0].GetString());
        Assert.Equal(resx["Words_And"], root.GetProperty("and").GetString());
        Assert.Single(root.GetProperty("dinar").EnumerateArray());
    }

    [Fact]
    public void TheFileHoldsNoNumberTheCardDoesNotHave()
    {
        var resx = Resx();
        using var words = Words();

        foreach (var property in words.RootElement.EnumerateObject())
        {
            if (int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                Assert.True(resx.ContainsKey("Words_" + property.Name), property.Name);
        }
    }
}
