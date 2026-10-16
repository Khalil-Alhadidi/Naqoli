extern alias AgentWeb;

using Agent.Core.Text;
using AgentWeb::Agent.Web.Resources;
using AgentWeb::Agent.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Agent.Core.Tests.Web;

/// <summary>
/// The amount in words from the real resource file: the parts are built by <c>AmountWordParts.FromResources</c> and
/// Khalil's examples (17 Oct 2026) are checked by composing the expected text from the same resource values, never from
/// a literal (no Arabic in C#).
/// </summary>
public sealed class AmountWordsResourceTests : IDisposable
{
    private readonly ServiceProvider provider = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();

    public void Dispose() => provider.Dispose();

    private IStringLocalizer<PhoneStrings> Text => provider.GetRequiredService<IStringLocalizer<PhoneStrings>>();

    private string W(string key)
    {
        var value = Text["Words_" + key];
        Assert.False(value.ResourceNotFound, "Words_" + key);
        return value.Value;
    }

    private string? Compose(long dinars) => AmountWords.Compose(dinars, AmountWordParts.FromResources(Text));

    [Fact]
    public void KhalilsExamplesComposeFromTheResourceValues()
    {
        var and = W("And");

        Assert.Equal(W("50") + " " + W("Thousand"), Compose(50_000));
        Assert.Equal(W("TwoThousand") + " " + and + W("500"), Compose(2_500));
        Assert.Equal(W("100") + " " + and + W("50") + " " + W("Thousand"), Compose(150_000));
        Assert.Equal(W("Million") + " " + and + W("200") + " " + W("Thousand"), Compose(1_200_000));
    }

    [Fact]
    public void TheOtherAmountsOfTheBriefComposeFromTheResourceValues()
    {
        var and = W("And");

        Assert.Equal(W("11") + " " + W("Thousand"), Compose(11_000));
        Assert.Equal(W("1") + " " + and + W("50") + " " + W("Thousand"), Compose(51_000));
        Assert.Equal(W("Million"), Compose(1_000_000));
        Assert.Equal(W("TwoMillion") + " " + and + W("500") + " " + W("Thousand"), Compose(2_500_000));
        Assert.Equal(W("Construct_3") + " " + W("Millions"), Compose(3_000_000));
        Assert.Equal(W("1") + " " + and + W("20") + " " + W("Thousand"), Compose(21_000));
        Assert.Equal(W("Construct_3") + " " + W("Thousands"), Compose(3_000));
        Assert.Equal(W("Construct_10") + " " + W("Thousands"), Compose(10_000));
        Assert.Equal(W("Million") + " " + and + W("Thousand"), Compose(1_001_000));
        Assert.Null(Compose(999));
        Assert.Null(Compose(100_000_000));
    }

    [Fact]
    public void EveryWordKeyIsInTheResourceFile()
    {
        string[] keys =
        [
            .. Enumerable.Range(1, 19).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            .. Enumerable.Range(2, 8).Select(i => (i * 10).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            .. Enumerable.Range(1, 9).Select(i => (i * 100).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            .. Enumerable.Range(3, 8).Select(i => "Construct_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "Thousand", "TwoThousand", "Thousands", "Million", "TwoMillion", "Millions", "And",
        ];

        foreach (var key in keys)
            Assert.False(string.IsNullOrWhiteSpace(W(key)), key);
        Assert.False(Text["Card_AmountWords"].ResourceNotFound);
    }
}
