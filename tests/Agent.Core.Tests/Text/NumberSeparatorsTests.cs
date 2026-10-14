using System.Text.RegularExpressions;
using Agent.Core.Text;

namespace Agent.Core.Tests.Text;

/// <summary>Review I1: the masker's pattern and the phone rule's test are the same set of separators.</summary>
public class NumberSeparatorsTests
{
    [Fact]
    public void ThePatternAndTheTestAgreeOnEveryCharacter()
    {
        var pattern = new Regex("^" + NumberSeparators.Pattern + "$", RegexOptions.CultureInvariant);

        for (var code = 0; code <= char.MaxValue; code++)
        {
            var c = (char)code;
            if (char.IsSurrogate(c))
                continue;
            Assert.True(pattern.IsMatch(c.ToString()) == NumberSeparators.IsSeparator(c), "Disagreement on U+" + code.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData(',', false)]
    [InlineData('0', false)]
    [InlineData('a', false)]
    [InlineData('-', true)]
    [InlineData('(', true)]
    public void ACommaIsNotASeparator(char c, bool expected)
    {
        Assert.Equal(expected, NumberSeparators.IsSeparator(c));
    }
}
