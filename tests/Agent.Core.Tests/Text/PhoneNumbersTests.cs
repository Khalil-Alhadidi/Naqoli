using Agent.Core.Text;

namespace Agent.Core.Tests.Text;

/// <summary>Rule 20: the country code makes no difference, spaces and dashes are ignored, 11 digits starting 07.</summary>
public class PhoneNumbersTests
{
    [Theory]
    [InlineData("+9647805550190")]
    [InlineData("07805550190")]
    [InlineData("009647805550190")]
    [InlineData("9647805550190")]
    [InlineData("0780 555 0190")]
    [InlineData("0780-555-0190")]
    [InlineData("+964 (780) 555.0190")]
    [InlineData("\u0660\u0667\u0668\u0660\u0665\u0665\u0665\u0660\u0661\u0669\u0660")]
    [InlineData("\u06F0\u06F7\u06F8\u06F0\u06F5\u06F5\u06F5\u06F0\u06F1\u06F9\u06F0")]
    public void SameAccountWithOrWithoutTheCountryCode(string typed)
    {
        Assert.True(PhoneNumbers.TryNormalize(typed, out var phone));
        Assert.Equal("07805550190", phone);
    }

    [Theory]
    [InlineData("078055501")]
    [InlineData("078055501900")]
    [InlineData("08805550190")]
    [InlineData("+9617805550190")]
    [InlineData("0780555019a")]
    [InlineData("")]
    public void AnythingElseIsNotACompleteNumber(string typed)
    {
        Assert.False(PhoneNumbers.TryNormalize(typed, out var phone));
        Assert.Equal(string.Empty, phone);
    }

    [Theory]
    [InlineData("07805550190", true)]
    [InlineData("078055501", true)]
    [InlineData("+964 780-555", true)]
    [InlineData("\u0660\u0667\u0668\u0660\u0665\u0665\u0665", true)]
    [InlineData("123456", false)]
    [InlineData("0780abc5550", false)]
    [InlineData("", false)]
    public void LooksLikeNumberNeedsSevenDigits(string typed, bool expected)
        => Assert.Equal(expected, PhoneNumbers.LooksLikeNumber(typed));
}
