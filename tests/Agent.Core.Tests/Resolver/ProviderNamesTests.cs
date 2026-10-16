using Agent.Core.Resolver;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Resolver;

/// <summary>
/// Ruling 1 of milestone 8 (16 Oct 2026): a biller's provider names are the parenthesised parts of its Arabic and English
/// names (the first word inside the parentheses); a name with no parentheses gives none. Seed text comes from the seed.
/// </summary>
public class ProviderNamesTests
{
    private static string ArabicProvider()
    {
        var name = SeedFacts.Seed.Billers.Single(b => b.Code == "internet").NameAr;
        var open = name.IndexOf('(', StringComparison.Ordinal);
        return name[(open + 1)..name.IndexOf(')', open)].Trim();
    }

    [Fact]
    public void TheInternetBillerNamesItsProviderInBothLanguages()
    {
        var bill = SeedWallet.BillsOf("U1").Single(b => b.Code == "BS4");

        Assert.Equal([ArabicProvider(), "Earthlink"], ProviderNames.Of(bill));
    }

    [Theory]
    [InlineData("BS1")]
    [InlineData("BS3")]
    public void ABillerWithNoParenthesesHasNoProviderName(string billCode)
        => Assert.Empty(ProviderNames.Of(SeedWallet.BillsOf("U1").Single(b => b.Code == billCode)));

    [Fact]
    public void AnEmptyOrUnclosedParenthesisGivesNoName()
    {
        var bill = SeedWallet.BillsOf("U1").Single(b => b.Code == "BS4");

        Assert.Empty(ProviderNames.Of(bill with { BillerName = "Internet ( )", BillerNameEn = "Internet (Earthlink" }));
    }

    [Theory]
    [InlineData("earth link", true)]
    [InlineData("pay the earth link subscription", true)]
    [InlineData("EARTHLINK", true)]
    [InlineData("earth", false)]
    [InlineData("earthlinks", false)]
    [InlineData("pay the bill", false)]
    [InlineData("", false)]
    public void AnyRunOfOneToThreeWordsIsComparedWithSpacesIgnored(string text, bool named)
    {
        // Khalil, 16 Oct 2026: provider names compare as contact names do (rule 1), spaces inside a name ignored.
        Assert.Equal(named, ProviderNames.AnyNamed(text, ["Earthlink"]));
    }

    [Fact]
    public void ARunLongerThanThreeWordsIsNotTried()
        => Assert.False(ProviderNames.AnyNamed("e a r th", ["earth"]));
}
