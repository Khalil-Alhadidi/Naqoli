using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Tests.Wallet;
using Agent.Core.Wallet;

namespace Agent.Core.Tests.Resolver;

/// <summary>Matching rules 13, 14 and 17 and D9 on the seed bills of U1, U2 and U3.</summary>
public class BillResolverTests
{
    private static readonly AgentOptions Options = new();

    private const string Alef = "ا";  // ARABIC LETTER ALEF
    private const string Lam = "ل";   // ARABIC LETTER LAM

    private static BillResolution Resolve(string userCode, string? billerCode, string? label = null, string? billerText = null)
        => BillResolver.Resolve(
            ParsedRequest.Bill(billerCode, label, null, AmountUnit.None, billerText),
            SeedWallet.BillsOf(userCode),
            SeedWallet.Products(),
            Options);

    private static string InternetNameAr => SeedFacts.Seed.Billers.Single(b => b.Code == "internet").NameAr;

    private static string InternetNameEn => SeedFacts.Seed.Billers.Single(b => b.Code == "internet").NameEn;

    /// <summary>The first word inside the parentheses of <paramref name="name"/> (ruling 1 of milestone 8).</summary>
    private static string Provider(string name)
    {
        var open = name.IndexOf('(', StringComparison.Ordinal);
        return name[(open + 1)..name.IndexOf(')', open)].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }

    /// <summary>The biller's name with its parenthesised part removed: the service, not the provider.</summary>
    private static string WithoutProvider(string name) => name[..name.IndexOf('(', StringComparison.Ordinal)].Trim();

    private static string[] Codes(IEnumerable<WalletBill> bills) => [.. bills.Select(b => b.Code)];

    private static string[] Codes(IEnumerable<WalletProduct> products) => [.. products.Select(p => p.Code)];

    [Fact]
    public void U1ElectricityAsksWhichOfTwoBills()
    {
        var options = Assert.IsType<BillResolution.Options>(Resolve("U1", "electricity"));
        Assert.Equal(["BS1", "BS2"], Codes(options.Bills));
    }

    [Fact]
    public void U1ElectricityWithTheShopLabelIsTheShopBill()
    {
        var card = Assert.IsType<BillResolution.Card>(Resolve("U1", "electricity", SeedWallet.LabelOf("BS2")));
        Assert.Equal("BS2", card.Bill.Code);
    }

    [Fact]
    public void LabelThatMatchesNoBillAsks()
    {
        // A label text that matches neither of U1's electricity labels still asks.
        var options = Assert.IsType<BillResolution.Options>(Resolve("U1", "electricity", "water"));
        Assert.Equal(["BS1", "BS2"], Codes(options.Bills));
    }

    [Fact]
    public void U1InternetAsksBillOrStore()
    {
        var ask = Assert.IsType<BillResolution.AskBillOrStore>(Resolve("U1", "internet"));
        Assert.Equal("BS4", ask.Bill.Code);
        Assert.Equal(["ZN-NET-10", "ZN-NET-25", "ZN-NET-40", "AC-NET-10", "AC-NET-25", "KR-NET-15", "KR-NET-30"], Codes(ask.InternetCards));
    }

    [Fact]
    public void U2InternetListsTheSevenStoreInternetCards()
    {
        var store = Assert.IsType<BillResolution.StoreInternet>(Resolve("U2", "internet"));
        Assert.Equal(7, store.InternetCards.Count);
        Assert.All(store.InternetCards, p => Assert.Equal("internet", p.Type));
    }

    [Fact]
    public void U1WaterIsTheWaterBill()
    {
        var card = Assert.IsType<BillResolution.Card>(Resolve("U1", "water"));
        Assert.Equal("BS3", card.Bill.Code);
    }

    [Fact]
    public void U2ElectricityHasNothingDue()
    {
        var nothing = Assert.IsType<BillResolution.NothingDue>(Resolve("U2", "electricity"));
        Assert.Equal("BZ1", nothing.Bill.Code);
    }

    [Fact]
    public void U1SchoolFeesIsNotFound()
        => Assert.IsType<BillResolution.NotFound>(Resolve("U1", "school_fees"));

    [Fact]
    public void U1NoBillerListsFourBills()
    {
        var options = Assert.IsType<BillResolution.Options>(Resolve("U1", null));
        Assert.Equal(["BS1", "BS2", "BS3", "BS4"], Codes(options.Bills));
    }

    [Fact]
    public void U2NoBillerListsOnlyBillsWithAnAmountDue()
    {
        var options = Assert.IsType<BillResolution.Options>(Resolve("U2", null));
        Assert.Equal(["BZ2", "BZ3"], Codes(options.Bills));
    }

    [Fact]
    public void NoBillerWithOneBillDueIsThatBill()
    {
        var bills = SeedWallet.BillsOf("U2").Where(b => b.Code != "BZ3").ToList();
        var card = Assert.IsType<BillResolution.Card>(BillResolver.Resolve(
            ParsedRequest.Bill(null, null, null, AmountUnit.None), bills, SeedWallet.Products(), Options));
        Assert.Equal("BZ2", card.Bill.Code);
    }

    [Fact]
    public void NoBillerWithNothingDueIsNothingDueOfTheFirstBill()
    {
        // Khalil, 14 Oct 2026: bills exist but none is due, so nothing_due, not bill_not_found.
        var bills = SeedWallet.BillsOf("U2").Where(b => b.Code == "BZ1").ToList();
        Assert.Equal(0, Assert.Single(bills).AmountDueIqd);
        var nothing = Assert.IsType<BillResolution.NothingDue>(BillResolver.Resolve(
            ParsedRequest.Bill(null, null, null, AmountUnit.None), bills, SeedWallet.Products(), Options));
        Assert.Equal("BZ1", nothing.Bill.Code);
    }

    [Fact]
    public void NoBillerWithNoBillsIsNotFound()
        => Assert.IsType<BillResolution.NotFound>(BillResolver.Resolve(
            ParsedRequest.Bill(null, null, null, AmountUnit.None), [], SeedWallet.Products(), Options));

    [Fact]
    public void InternetBillWithNothingDueIsNothingDue()
    {
        var bills = SeedWallet.BillsOf("U1").Select(b => b.Code == "BS4" ? b with { AmountDueIqd = 0 } : b).ToList();
        var nothing = Assert.IsType<BillResolution.NothingDue>(BillResolver.Resolve(
            ParsedRequest.Bill("internet", null, null, AmountUnit.None), bills, SeedWallet.Products(), Options));
        Assert.Equal("BS4", nothing.Bill.Code);
    }

    [Fact]
    public void LabelledBillWithNothingDueIsNothingDue()
    {
        var bills = SeedWallet.BillsOf("U1").Select(b => b.Code == "BS2" ? b with { AmountDueIqd = 0 } : b).ToList();
        var nothing = Assert.IsType<BillResolution.NothingDue>(BillResolver.Resolve(
            ParsedRequest.Bill("electricity", SeedWallet.LabelOf("BS2"), null, AmountUnit.None), bills, SeedWallet.Products(), Options));
        Assert.Equal("BS2", nothing.Bill.Code);
    }

    [Fact]
    public void SeveralBillsOfABillerAreAllOfferedDueOrNot()
    {
        var bills = SeedWallet.BillsOf("U1").Select(b => b.Code == "BS1" ? b with { AmountDueIqd = 0 } : b).ToList();
        var options = Assert.IsType<BillResolution.Options>(BillResolver.Resolve(
            ParsedRequest.Bill("electricity", null, null, AmountUnit.None), bills, SeedWallet.Products(), Options));
        Assert.Equal(["BS1", "BS2"], Codes(options.Bills));
    }

    [Fact]
    public void U3ElectricityIsTheOneBill()
    {
        var card = Assert.IsType<BillResolution.Card>(Resolve("U3", "electricity"));
        Assert.Equal("BT1", card.Bill.Code);
    }

    [Fact]
    public void U1InternetNamingTheProviderInArabicIsTheCard()
    {
        var card = Assert.IsType<BillResolution.Card>(Resolve("U1", "internet", billerText: Provider(InternetNameAr)));
        Assert.Equal("BS4", card.Bill.Code);
    }

    [Fact]
    public void U1InternetNamingTheProviderAmongOtherWordsIsTheCard()
    {
        var text = WithoutProvider(InternetNameAr) + " " + Provider(InternetNameAr);
        var card = Assert.IsType<BillResolution.Card>(Resolve("U1", "internet", billerText: text));
        Assert.Equal("BS4", card.Bill.Code);
    }

    [Fact]
    public void U1InternetNamingTheProviderWithALeadingAlIsTheCard()
    {
        var card = Assert.IsType<BillResolution.Card>(Resolve("U1", "internet", billerText: Alef + Lam + Provider(InternetNameAr)));
        Assert.Equal("BS4", card.Bill.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void U1InternetNamingTheProviderInEnglishInAnyCaseIsTheCard(bool upper)
    {
        var provider = Provider(InternetNameEn);
        var card = Assert.IsType<BillResolution.Card>(
            Resolve("U1", "internet", billerText: upper ? provider.ToUpperInvariant() : provider.ToLowerInvariant()));
        Assert.Equal("BS4", card.Bill.Code);
    }

    [Fact]
    public void U1InternetWithWordsThatNameNoProviderAsksBillOrStore()
    {
        var ask = Assert.IsType<BillResolution.AskBillOrStore>(Resolve("U1", "internet", billerText: WithoutProvider(InternetNameAr)));
        Assert.Equal("BS4", ask.Bill.Code);
        Assert.IsType<BillResolution.AskBillOrStore>(Resolve("U1", "internet", billerText: WithoutProvider(InternetNameEn)));
    }

    [Fact]
    public void AProviderNameOnlyPartOfAWordIsNotTheProvider()
    {
        var provider = Provider(InternetNameAr);
        Assert.IsType<BillResolution.AskBillOrStore>(Resolve("U1", "internet", billerText: provider[..^1]));
        Assert.IsType<BillResolution.AskBillOrStore>(Resolve("U1", "internet", billerText: Provider(InternetNameEn) + "x"));
    }

    [Fact]
    public void AProviderNameDoesNotChangeANonInternetBiller()
    {
        var provider = Provider(InternetNameAr);
        Assert.Equal("BS3", Assert.IsType<BillResolution.Card>(Resolve("U1", "water", billerText: provider)).Bill.Code);
        Assert.Equal(["BS1", "BS2"], Codes(Assert.IsType<BillResolution.Options>(Resolve("U1", "electricity", billerText: provider)).Bills));
        Assert.IsType<BillResolution.StoreInternet>(Resolve("U2", "internet", billerText: provider));
    }

    [Fact]
    public void AProviderNameDoesNotMakeANothingDueInternetBillACard()
    {
        var bills = SeedWallet.BillsOf("U1").Select(b => b.Code == "BS4" ? b with { AmountDueIqd = 0 } : b).ToList();
        var nothing = Assert.IsType<BillResolution.NothingDue>(BillResolver.Resolve(
            ParsedRequest.Bill("internet", null, null, AmountUnit.None, Provider(InternetNameAr)), bills, SeedWallet.Products(), Options));
        Assert.Equal("BS4", nothing.Bill.Code);
    }
}
