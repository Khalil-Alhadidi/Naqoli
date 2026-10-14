using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Wallet;

namespace Agent.Core.Tests.Resolver;

/// <summary>Matching rules 13, 14 and 17 and D9 on the seed bills of U1, U2 and U3.</summary>
public class BillResolverTests
{
    private static readonly AgentOptions Options = new();

    private static BillResolution Resolve(string userCode, string? billerCode, string? label = null)
        => BillResolver.Resolve(
            ParsedRequest.Bill(billerCode, label, null, AmountUnit.None), SeedWallet.BillsOf(userCode), SeedWallet.Products(), Options);

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
    public void NoBillerWithNothingDueIsNotFound()
    {
        var bills = SeedWallet.BillsOf("U2").Where(b => b.Code == "BZ1").ToList();
        Assert.IsType<BillResolution.NotFound>(BillResolver.Resolve(
            ParsedRequest.Bill(null, null, null, AmountUnit.None), bills, SeedWallet.Products(), Options));
    }

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
}
