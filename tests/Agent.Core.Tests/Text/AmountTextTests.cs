using System.Text.Json;
using Agent.Core.Requests;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;

namespace Agent.Core.Tests.Text;

/// <summary>
/// The code reader of a typed amount (17 Oct 2026) over <c>amount-text-examples.json</c>: Khalil's three examples
/// (<c>source: khalil</c>) and the ones composed from his number words for him to check (<c>source: composed</c>).
/// </summary>
public class AmountTextTests
{
    private sealed record AmountExample(string Source, string Text, bool Reads, decimal? Value, string? Unit);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static IReadOnlyList<AmountExample> Examples { get; } = JsonSerializer.Deserialize<List<AmountExample>>(
        File.ReadAllText(Path.Combine(SeedHashTests.RepositoryRoot(), "tests", "Agent.Core.Tests", "Text", "amount-text-examples.json")),
        JsonOptions)!;

    public static TheoryData<int> ExampleIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Examples.Count; i++)
            data.Add(i);
        return data;
    }

    [Fact]
    public void KhalilsThreeExamplesAreInTheFile()
        => Assert.Equal(3, Examples.Count(e => e.Source == "khalil"));

    [Theory]
    [MemberData(nameof(ExampleIndexes))]
    public void EveryExampleReadsAsExpected(int index)
    {
        var example = Examples[index];

        var reads = AmountText.TryRead(example.Text, out var value, out var unit);

        Assert.True(example.Reads == reads, $"example {index} ({example.Source}): reads {reads}");
        if (!example.Reads)
            return;
        Assert.Equal(example.Value, value);
        Assert.Equal(ParseUnit(example.Unit!), unit);
    }

    [Theory]
    [InlineData("12,34")]
    [InlineData("1,2345")]
    [InlineData("50 dinar")]
    [InlineData("send 50")]
    public void ANumberWithAnOddSeparatorOrAnotherWordDoesNotRead(string text)
        => Assert.False(AmountText.TryRead(text, out _, out _));

    [Fact]
    public void ArabicIndicAndEasternDigitsReadAsAsciiDigits()
    {
        Assert.True(AmountText.TryRead(ArabicDigits.ToArabicIndic("75"), out var arabicIndic, out var unit));
        Assert.Equal((75m, AmountUnit.None), (arabicIndic, unit));
        Assert.True(AmountText.TryRead("\u06F7\u06F5", out var eastern, out _));
        Assert.Equal(75m, eastern);
    }

    private static AmountUnit ParseUnit(string unit) => unit switch
    {
        "none" => AmountUnit.None,
        "dinars" => AmountUnit.Dinars,
        "thousand" => AmountUnit.Thousand,
        "million" => AmountUnit.Million,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown unit."),
    };
}
