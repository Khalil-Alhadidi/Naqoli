using Agent.Core.Text;

namespace Agent.Core.Tests.Text;

/// <summary>
/// The amount in words (Khalil's list and examples, 17 Oct 2026), composed from a synthetic English parts table built
/// here, so the structure is tested without Arabic. The joiner is <c>&amp;</c>, attached to the following word as
/// Khalil's examples attach theirs.
/// </summary>
public class AmountWordsTests
{
    private static readonly AmountWordParts Parts = new(
        Units: ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"],
        Tens: ["twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"],
        Hundreds: ["hundred", "two-hundred", "three-hundred", "four-hundred", "five-hundred", "six-hundred",
            "seven-hundred", "eight-hundred", "nine-hundred"],
        Thousand: "thousand",
        TwoThousand: "two-thousand",
        Thousands: "thousands",
        Million: "million",
        TwoMillion: "two-million",
        Millions: "millions",
        Constructs: ["three-c", "four-c", "five-c", "six-c", "seven-c", "eight-c", "nine-c", "ten-c"],
        And: "&");

    [Theory]
    [InlineData(2_500, "two-thousand &five-hundred")]
    [InlineData(11_000, "eleven thousand")]
    [InlineData(50_000, "fifty thousand")]
    [InlineData(51_000, "one &fifty thousand")]
    [InlineData(150_000, "hundred &fifty thousand")]
    [InlineData(1_000_000, "million")]
    [InlineData(1_200_000, "million &two-hundred thousand")]
    [InlineData(2_500_000, "two-million &five-hundred thousand")]
    [InlineData(3_000_000, "three-c millions")]
    [InlineData(21_000, "one &twenty thousand")]
    [InlineData(3_000, "three-c thousands")]
    [InlineData(10_000, "ten-c thousands")]
    [InlineData(1_001_000, "million &thousand")]
    [InlineData(1_000, "thousand")]
    [InlineData(2_000, "two-thousand")]
    [InlineData(25_025, "five &twenty thousand &five &twenty")]
    [InlineData(100_000, "hundred thousand")]
    [InlineData(10_000_000, "ten-c millions")]
    [InlineData(11_000_000, "eleven million")]
    [InlineData(99_999_999, "nine &ninety million &nine-hundred &nine &ninety thousand &nine-hundred &nine &ninety")]
    [InlineData(1_000_150, "million &hundred &fifty")]
    public void TheAmountIsComposedAsKhalilsRulesSay(long dinars, string expected)
    {
        Assert.Equal(expected, AmountWords.Compose(dinars, Parts));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5_000)]
    [InlineData(999)]
    [InlineData(100_000_000)]
    [InlineData(long.MaxValue)]
    public void AnAmountOutsideTheRangeHasNoWords(long dinars)
    {
        Assert.Null(AmountWords.Compose(dinars, Parts));
    }

    [Fact]
    public void APartsTableOfTheWrongShapeIsRefused()
    {
        var shortUnits = Parts with { Units = ["one"] };

        Assert.Throws<ArgumentException>(() => AmountWords.Compose(50_000, shortUnits));
    }
}
