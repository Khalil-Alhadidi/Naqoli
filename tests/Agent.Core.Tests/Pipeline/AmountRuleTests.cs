using Agent.Core.Pipeline;
using Agent.Core.Requests;

namespace Agent.Core.Tests.Pipeline;

/// <summary>Matching rules 9 and 10: how the amount of a request is read.</summary>
public class AmountRuleTests
{
    private static readonly AgentOptions Options = new();

    private static AmountResolution Resolve(decimal? value, AmountUnit unit)
        => AmountRule.Resolve(ParsedRequest.Transfer("x", value, unit), Options);

    [Fact]
    public void BareFiftyAsksOnlyTheThousandReading()
    {
        // Rule 9: 50,000,000 is over the per-transaction cap, so only 50,000 is offered.
        var ask = Assert.IsType<AmountResolution.Ask>(Resolve(50m, AmountUnit.None));
        Assert.Equal([50_000L], ask.Options);
    }

    [Fact]
    public void BareTwoAndAHalfAsksBothReadings()
    {
        var ask = Assert.IsType<AmountResolution.Ask>(Resolve(2.5m, AmountUnit.None));
        Assert.Equal([2_500L, 2_500_000L], ask.Options);
    }

    [Fact]
    public void BareNumberOfAThousandOrMoreIsDinars()
    {
        Assert.Equal(new AmountResolution.Dinars(25_000), Resolve(25_000m, AmountUnit.None));
        Assert.Equal(new AmountResolution.Dinars(1_000), Resolve(1_000m, AmountUnit.None));
    }

    [Fact]
    public void BareNumberJustBelowAThousandAsks()
    {
        var ask = Assert.IsType<AmountResolution.Ask>(Resolve(999m, AmountUnit.None));
        Assert.Equal([999_000L], ask.Options);
    }

    [Fact]
    public void ReadingsBelowTheMinimumAreDropped()
    {
        // Khalil, 14 Oct 2026: 0.5 thousand is 500, below the minimum of 1,000, so only 500,000 is offered.
        var ask = Assert.IsType<AmountResolution.Ask>(Resolve(0.5m, AmountUnit.None));
        Assert.Equal([500_000L], ask.Options);
    }

    [Fact]
    public void NoReadingAtOrAboveTheMinimumIsAmountBelowMinimum()
    {
        // 0.0005 thousand is half a dinar (not whole); 0.0005 million is 500, below the minimum.
        Assert.Equal(new AmountResolution.Invalid(ReasonCodes.AmountBelowMinimum), Resolve(0.0005m, AmountUnit.None));
    }

    [Fact]
    public void ReadingsFollowTheConfiguredMinimum()
    {
        var options = new AgentOptions { MinAmountIqd = 100 };
        var ask = Assert.IsType<AmountResolution.Ask>(AmountRule.Resolve(ParsedRequest.Transfer("x", 0.5m, AmountUnit.None), options));
        Assert.Equal([500L, 500_000L], ask.Options);
    }

    [Fact]
    public void BareNumberWithNoUsableReadingIsNotUnderstood()
        => Assert.Equal(new AmountResolution.Invalid(ReasonCodes.NotUnderstood), Resolve(0.0000001m, AmountUnit.None));

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

    [Fact]
    public void ReadingsFollowTheConfiguredCap()
    {
        var options = new AgentOptions { PerTransactionCapIqd = 1_000_000 };
        var ask = Assert.IsType<AmountResolution.Ask>(AmountRule.Resolve(ParsedRequest.Transfer("x", 2.5m, AmountUnit.None), options));
        Assert.Equal([2_500L], ask.Options);
    }
}
