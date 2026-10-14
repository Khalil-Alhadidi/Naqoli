using Agent.Core.Masking;
using Agent.Core.Text;

namespace Agent.Core.Tests.Masking;

/// <summary>
/// Rule 5: every run of seven or more digits becomes a token before the text reaches the parse call, and only this
/// message's map turns a token back into the number. Arabic-Indic digits are built from ASCII, so the file holds no Arabic.
/// </summary>
public class MaskerTests
{
    private const string Phone = "07701234567";

    /// <summary>Extended Arabic-Indic digits (U+06F0 to U+06F9), built by char arithmetic.</summary>
    private static string ExtendedArabicIndic(string ascii)
        => string.Concat(ascii.Select(c => char.IsAsciiDigit(c) ? (char)(0x06F0 + (c - '0')) : c));

    [Fact]
    public void AnAsciiNumberBecomesAToken()
    {
        var masked = Masker.Mask("send 50000 to " + Phone + " now");

        Assert.Equal("send 50000 to <NUM_1> now", masked.Text);
        Assert.True(masked.Map.TryUnmask("<NUM_1>", out var original));
        Assert.Equal(Phone, original);
        Assert.Equal(1, masked.Map.Count);
    }

    [Fact]
    public void AnArabicIndicNumberBecomesAToken()
    {
        var typed = ArabicDigits.ToArabicIndic(Phone);

        var masked = Masker.Mask("x " + typed);

        Assert.Equal("x <NUM_1>", masked.Text);
        Assert.True(masked.Map.TryUnmask("<NUM_1>", out var original));
        Assert.Equal(typed, original);
    }

    [Fact]
    public void AnExtendedArabicIndicNumberBecomesAToken()
    {
        var typed = ExtendedArabicIndic(Phone);

        var masked = Masker.Mask(typed + " x");

        Assert.Equal("<NUM_1> x", masked.Text);
        Assert.True(masked.Map.TryUnmask("<NUM_1>", out var original));
        Assert.Equal(typed, original);
    }

    [Theory]
    [InlineData("+964 770 123 4567")]
    [InlineData("+9647701234567")]
    [InlineData("00964 770 123 4567")]
    [InlineData("0770 123 4567")]
    [InlineData("0770-123-4567")]
    [InlineData("0770.123.4567")]
    [InlineData("0770 - 123 - 4567")]
    [InlineData("1234567")]
    public void ARunWithItsSeparatorsAndPlusIsOneToken(string number)
    {
        var masked = Masker.Mask("to " + number + ".");

        Assert.Equal("to <NUM_1>.", masked.Text);
        Assert.True(masked.Map.TryUnmask("<NUM_1>", out var original));
        Assert.Equal(number, original);
    }

    /// <summary>A phone number split by <paramref name="separator"/> between its groups.</summary>
    internal static string Split(int separator)
        => "0770" + (char)separator + "123" + (char)separator + "4567";

    [Theory]
    [InlineData(0x00A0)] // no-break space
    [InlineData(0x0009)] // tab
    [InlineData(0x000A)] // line feed
    [InlineData(0x2009)] // thin space
    [InlineData(0x200E)] // left-to-right mark
    [InlineData(0x200F)] // right-to-left mark
    [InlineData(0x061C)] // Arabic letter mark (a format character)
    public void AnySeparatorOfAPhoneNumberIsInsideTheRun(int separator)
    {
        // Review I1: the same separators as PhoneNumbers (Unicode whitespace, format characters, dashes, dots, brackets).
        var number = Split(separator);

        var masked = Masker.Mask("to " + number + " now");

        Assert.Equal("to <NUM_1> now", masked.Text);
        Assert.True(masked.Map.TryUnmask("<NUM_1>", out var original));
        Assert.Equal(number, original);
    }

    [Fact]
    public void BracketsAroundANumberStayOutsideTheToken()
    {
        var masked = Masker.Mask("to (0770) (123) (4567).");

        Assert.Equal("to (<NUM_1>).", masked.Text);
        Assert.True(masked.Map.TryUnmask("<NUM_1>", out var original));
        Assert.Equal("0770) (123) (4567", original);
    }

    [Fact]
    public void TwoNumbersAreTwoTokensInOrder()
    {
        var masked = Masker.Mask("from 07801112222 to 0790-333-4444");

        Assert.Equal("from <NUM_1> to <NUM_2>", masked.Text);
        Assert.True(masked.Map.TryUnmask("<NUM_1>", out var first));
        Assert.True(masked.Map.TryUnmask("<NUM_2>", out var second));
        Assert.Equal(("07801112222", "0790-333-4444"), (first, second));
        Assert.Equal(2, masked.Map.Count);
    }

    [Theory]
    [InlineData("send 50000 to my brother")]
    [InlineData("send 150000")]
    [InlineData("send 1,000,000")]
    [InlineData("pay 123456 now")]
    [InlineData("no digits at all")]
    [InlineData("")]
    public void FewerThanSevenDigitsAreKept(string text)
    {
        var masked = Masker.Mask(text);

        Assert.Equal(text, masked.Text);
        Assert.Equal(0, masked.Map.Count);
    }

    [Fact]
    public void ArabicIndicAmountsBelowSevenDigitsAreKept()
    {
        var text = ArabicDigits.ToArabicIndic("50000");

        Assert.Equal(text, Masker.Mask(text).Text);
    }

    [Theory]
    [InlineData("<NUM_2>")]
    [InlineData("NUM_1")]
    [InlineData("<NUM_1> ")]
    [InlineData("<num_1>")]
    [InlineData("")]
    public void AnUnknownTokenDoesNotUnmask(string token)
    {
        var masked = Masker.Mask("to " + Phone);

        Assert.False(masked.Map.TryUnmask(token, out var original));
        Assert.Equal(string.Empty, original);
    }

    [Fact]
    public void TheEmptyMapUnmasksNothing()
    {
        Assert.False(MaskMap.Empty.TryUnmask("<NUM_1>", out _));
        Assert.Equal(0, MaskMap.Empty.Count);
    }

    [Theory]
    [InlineData("<NUM_1>", true)]
    [InlineData("<NUM_12>", true)]
    [InlineData("<NUM_0>", false)]
    [InlineData("x <NUM_1>", false)]
    [InlineData("<NUM_>", false)]
    [InlineData("<NUM_1>\n", false)]
    public void IsTokenMatchesExactlyOneToken(string text, bool expected)
    {
        Assert.Equal(expected, MaskMap.IsToken(text));
    }

    [Theory]
    [InlineData("07701234567", true)]
    [InlineData("call 0770 123 4567", true)]
    [InlineData("<NUM_1>", false)]
    [InlineData("Ali 123456", false)]
    public void HasNumberRunFindsSevenDigits(string text, bool expected)
    {
        Assert.Equal(expected, Masker.HasNumberRun(text));
    }
}
