using Agent.Core.Pipeline;
using Agent.Core.Requests;

namespace Agent.Core.Tests.Pipeline;

/// <summary>Matching rules 9 and 10: how the amount of a request is read.</summary>
public class AmountRuleTests
{
    private static AmountResolution Resolve(decimal? value, AmountUnit unit)
        => AmountRule.Resolve(ParsedRequest.Transfer("x", value, unit));

    [Fact]
    public void BareTwoAndAHalfIsTwoThousandFiveHundred()
        => Assert.Equal(new AmountResolution.Dinars(2_500), Resolve(2.5m, AmountUnit.None));

    [Fact]
    public void BareFiftyIsFiftyThousand()
        => Assert.Equal(new AmountResolution.Dinars(50_000), Resolve(50m, AmountUnit.None));

    [Fact]
    public void BareNumberJustBelowAThousandIsThousands()
        => Assert.Equal(new AmountResolution.Dinars(999_000), Resolve(999m, AmountUnit.None));

    [Fact]
    public void BareNumberOfAThousandOrMoreIsDinars()
    {
        Assert.Equal(new AmountResolution.Dinars(25_000), Resolve(25_000m, AmountUnit.None));
        Assert.Equal(new AmountResolution.Dinars(1_000), Resolve(1_000m, AmountUnit.None));
    }

    [Fact]
    public void ABareReadingBelowTheMinimumIsLeftToTheChecks()
    {
        // Rule 9 (Khalil, 16 Oct 2026): 0.5 is 500, below the minimum of 1,000. The reader does not drop it or ask;
        // the checks refuse it amount_below_minimum.
        Assert.Equal(new AmountResolution.Dinars(500), Resolve(0.5m, AmountUnit.None));
    }

    [Fact]
    public void BareTwoAndAQuarterIsTwoThousandTwoHundredFifty()
        => Assert.Equal(new AmountResolution.Dinars(2_250), Resolve(2.25m, AmountUnit.None));

    [Fact]
    public void AQuarterAfterTheUnitIsAQuarterOfIt()
    {
        // Quarters (Khalil, 22 Oct 2026): twelve thousand and a quarter is 12.25 thousand, a million and a quarter
        // is 1.25 million.
        Assert.Equal(new AmountResolution.Dinars(12_250), Resolve(12.25m, AmountUnit.Thousand));
        Assert.Equal(new AmountResolution.Dinars(1_250_000), Resolve(1.25m, AmountUnit.Million));
    }

    [Fact]
    public void LessAQuarterOrAHalfTakesItAway()
    {
        // A million less a quarter is 0.75 million, two thousand less a quarter is 1.75 thousand, three less a
        // quarter is 2.75 bare, a million less a half is 0.5 million.
        Assert.Equal(new AmountResolution.Dinars(750_000), Resolve(0.75m, AmountUnit.Million));
        Assert.Equal(new AmountResolution.Dinars(1_750), Resolve(1.75m, AmountUnit.Thousand));
        Assert.Equal(new AmountResolution.Dinars(2_750), Resolve(2.75m, AmountUnit.None));
        Assert.Equal(new AmountResolution.Dinars(500_000), Resolve(0.5m, AmountUnit.Million));
    }

    [Fact]
    public void AQuarterOfAMillionIsTwoHundredFiftyThousand()
        => Assert.Equal(new AmountResolution.Dinars(250_000), Resolve(0.25m, AmountUnit.Million));

    [Fact]
    public void ABareQuarterIsLeftToTheChecks()
    {
        // A quarter alone is 0.25 bare, read in thousands: 250, below the minimum. The checks refuse it
        // amount_below_minimum. The refusal end to end is ADD-29 in the development set.
        Assert.Equal(new AmountResolution.Dinars(250), Resolve(0.25m, AmountUnit.None));
    }

    [Fact]
    public void AQuarterOfADinarIsNotUnderstood()
        => Assert.Equal(new AmountResolution.Invalid(ReasonCodes.NotUnderstood), Resolve(0.25m, AmountUnit.Dinars));

    [Fact]
    public void AReadingThatIsNotWholeDinarsIsNotUnderstood()
    {
        // 0.0005 thousand is half a dinar.
        Assert.Equal(new AmountResolution.Invalid(ReasonCodes.NotUnderstood), Resolve(0.0005m, AmountUnit.None));
        Assert.Equal(new AmountResolution.Invalid(ReasonCodes.NotUnderstood), Resolve(0.0000001m, AmountUnit.None));
    }

    [Fact]
    public void MillionOnlyWithTheUnit()
    {
        Assert.Equal(new AmountResolution.Dinars(2_000), Resolve(2m, AmountUnit.None));
        Assert.Equal(new AmountResolution.Dinars(2_000_000), Resolve(2m, AmountUnit.Million));
    }

    [Fact]
    public void ThousandAndMillionUnitsMultiply()
    {
        Assert.Equal(new AmountResolution.Dinars(50_000), Resolve(50m, AmountUnit.Thousand));
        Assert.Equal(new AmountResolution.Dinars(2_500), Resolve(2.5m, AmountUnit.Thousand));
        Assert.Equal(new AmountResolution.Dinars(1_500_000), Resolve(1.5m, AmountUnit.Million));
    }

    [Fact]
    public void DinarsUnitIsTheAmountAsWritten()
        => Assert.Equal(new AmountResolution.Dinars(500), Resolve(500m, AmountUnit.Dinars));

    [Fact]
    public void MissingAmountIsMissing()
        => Assert.IsType<AmountResolution.Missing>(Resolve(null, AmountUnit.None));

    [Theory]
    [InlineData(1500.5, AmountUnit.None)]
    [InlineData(25000.25, AmountUnit.Dinars)]
    [InlineData(2.0005, AmountUnit.Thousand)]
    [InlineData(1.0000005, AmountUnit.Million)]
    public void FractionalDinarsAreNotUnderstood(double value, AmountUnit unit)
        => Assert.Equal(new AmountResolution.Invalid(ReasonCodes.NotUnderstood), Resolve((decimal)value, unit));

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void ZeroOrNegativeIsNotUnderstood(int value)
        => Assert.Equal(new AmountResolution.Invalid(ReasonCodes.NotUnderstood), Resolve(value, AmountUnit.None));

    [Fact]
    public void AmountTooLargeForALongIsNotUnderstood()
        => Assert.Equal(new AmountResolution.Invalid(ReasonCodes.NotUnderstood), Resolve(decimal.MaxValue, AmountUnit.Million));
}
