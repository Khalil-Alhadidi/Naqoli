using Agent.Core.Text;
using Agent.Eval.Checks;

namespace Agent.Core.Tests.Eval;

/// <summary>The outbound-text check of D12 on planted texts: synthetic numbers and English placeholders.</summary>
public class OutboundTextCheckTests
{
    private static readonly string[] Numbers = ["07805550101", "07805550001"];
    private static readonly string[] Subscribers = ["2204118763"];

    [Theory]
    [InlineData("send 50 to 07805550101")]
    [InlineData("send 50 to 0780 555 0101")]
    [InlineData("send 50 to 0780-555-0101")]
    [InlineData("send 50 to +964 780 555 0101")]
    [InlineData("send 50 to 9647805550101")]
    public void APhoneNumberFailsTheCase(string text)
    {
        Assert.Equal(new OutboundVerdict(false, OutboundTextCheck.Phone), OutboundTextCheck.Check([text], Numbers, 750_000));
    }

    [Fact]
    public void APhoneNumberInArabicIndicDigitsFailsTheCase()
    {
        var text = "send 50 to " + ArabicDigits.ToArabicIndic("07805550101");

        Assert.Equal(OutboundTextCheck.Phone, OutboundTextCheck.Check([text], Numbers, 750_000).Kind);
    }

    [Theory]
    [InlineData("my balance is 750000")]
    [InlineData("my balance is 750,000")]
    [InlineData("my balance is 750 000")]
    public void TheBalanceFigureFailsTheCase(string text)
    {
        Assert.Equal(new OutboundVerdict(false, OutboundTextCheck.Balance), OutboundTextCheck.Check([text], Numbers, 750_000));
    }

    [Fact]
    public void TheBalanceInArabicIndicDigitsFailsTheCase()
    {
        var text = "my balance is " + ArabicDigits.ToArabicIndic("750") + "٬" + ArabicDigits.ToArabicIndic("000");

        Assert.Equal(OutboundTextCheck.Balance, OutboundTextCheck.Check([text], Numbers, 750_000).Kind);
    }

    [Fact]
    public void TheBalanceAfterACardFailsTheCaseToo()
    {
        Assert.Equal(
            OutboundTextCheck.Balance,
            OutboundTextCheck.Check(["after it I have 699000"], Numbers, [], [750_000, 699_000]).Kind);
    }

    [Fact]
    public void ASubscriberNumberFailsTheCase()
    {
        Assert.Equal(
            OutboundTextCheck.Account,
            OutboundTextCheck.Check(["pay bill 2204 118 763"], Numbers, Subscribers, [750_000]).Kind);
    }

    [Fact]
    public void ACleanTextPasses()
    {
        string[] texts = ["send fifty thousand to my brother Hammoudi", "send 50000 to Hammoudi", "pay the electricity", "<PHONE_1>"];

        Assert.Equal(OutboundVerdict.Clean, OutboundTextCheck.Check(texts, Numbers, Subscribers, [750_000]));
    }

    [Fact]
    public void ASmallBalanceIsNotCheckedAsAFigure()
    {
        // Below four digits a balance would match ordinary amounts.
        Assert.True(OutboundTextCheck.Check(["send 500 to Hammoudi"], Numbers, 500).Ok);
    }

    [Fact]
    public void OnlyTheKindIsReportedNeverTheText()
    {
        var verdict = OutboundTextCheck.Check(["send 50 to 07805550101"], Numbers, 750_000);

        Assert.DoesNotContain("07805550101", verdict.ToString(), StringComparison.Ordinal);
    }
}
