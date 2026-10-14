using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Resolver;

/// <summary>Matching rules 11 and 12 on the seed store catalogue.</summary>
public class ProductResolverTests
{
    private static readonly AgentOptions Options = new();

    private static ProductResolution Resolve(string? operatorText, string? type, long? value, AgentOptions? options = null)
        => ProductResolver.Resolve(ParsedRequest.Purchase(operatorText, type, value), SeedWallet.Products(), options ?? Options);

    /// <summary>Zain's other name in the seed (<c>also_called</c>), read from the file, never written here.</summary>
    private static string ZainOtherName => SeedFacts.Seed.Products.First(p => p.Provider == "zain").AlsoCalled!;

    private static string ArabicNameOf(string provider) => SeedFacts.Seed.Products.First(p => p.Provider == provider).ProviderNameAr;

    [Fact]
    public void InternetFortyWithNoOperatorAsksTheOperatorEvenWithOneFit()
    {
        // Rule 11: only Zain has a 40,000 internet card, and the agent still asks.
        var ask = Assert.IsType<ProductResolution.AskOperator>(Resolve(null, "internet", 40_000));
        Assert.Equal(["zain"], ask.ProviderCodes);
    }

    [Fact]
    public void CreditTenWithNoOperatorAsksAmongThree()
    {
        var ask = Assert.IsType<ProductResolution.AskOperator>(Resolve(null, "credit", 10_000));
        Assert.Equal(["zain", "asiacell", "korek"], ask.ProviderCodes);
    }

    [Fact]
    public void ZainCreditTenIsTheCard()
    {
        var card = Assert.IsType<ProductResolution.Card>(Resolve("zain", "credit", 10_000));
        Assert.Equal("ZN-CR-10", card.Product.Code);
    }

    [Fact]
    public void ZainCreditWithNoValueListsThreeProducts()
    {
        var options = Assert.IsType<ProductResolution.Options>(Resolve("zain", "credit", null));
        Assert.Equal(["ZN-CR-5", "ZN-CR-10", "ZN-CR-25"], options.Products.Select(p => p.Code));
    }

    [Fact]
    public void KorekCreditTwoAndAHalfIsNotFound()
        => Assert.IsType<ProductResolution.NotFound>(Resolve("korek", "credit", 2_500));

    [Fact]
    public void OtherNameMatchesZain()
    {
        var card = Assert.IsType<ProductResolution.Card>(Resolve(ZainOtherName, "credit", 10_000));
        Assert.Equal("ZN-CR-10", card.Product.Code);
    }

    [Theory]
    [InlineData("asiacell")]
    [InlineData("Asiacell")]
    [InlineData("ASIACELL")]
    public void EnglishNameMatchesInAnyCase(string typed)
    {
        var card = Assert.IsType<ProductResolution.Card>(Resolve(typed, "credit", 25_000));
        Assert.Equal("AC-CR-25", card.Product.Code);
    }

    [Fact]
    public void ArabicNameMatches()
    {
        var card = Assert.IsType<ProductResolution.Card>(Resolve(ArabicNameOf("korek"), "internet", 30_000));
        Assert.Equal("KR-NET-30", card.Product.Code);
    }

    [Fact]
    public void ZainTenWithNoTypeListsTwoProducts()
    {
        // Rule 12's example: a Zain card of ten lists two products.
        var options = Assert.IsType<ProductResolution.Options>(Resolve("zain", null, 10_000));
        Assert.Equal(["ZN-CR-10", "ZN-NET-10"], options.Products.Select(p => p.Code));
    }

    [Fact]
    public void NothingNamedAsksAmongEveryOperator()
    {
        // Rule 12: fifteen products fit, more than one question holds, so the operator comes first.
        var ask = Assert.IsType<ProductResolution.AskOperator>(Resolve(null, null, null));
        Assert.Equal(["zain", "asiacell", "korek"], ask.ProviderCodes);
    }

    [Fact]
    public void UnknownOperatorTextIsNotFound()
        => Assert.IsType<ProductResolution.NotFound>(Resolve("fastlink", "credit", 5_000)); // Khalil, 14 Oct 2026

    [Fact]
    public void OperatorTextNamingSeveralProvidersAsksAmongThem()
    {
        // Two providers that share another name: the typed name matches both, so the agent asks between them.
        const string shared = "shared name";
        var catalogue = SeedWallet.Products()
            .Select(p => p.Provider is "zain" or "korek" ? p with { AlsoCalled = shared } : p)
            .ToList();

        var ask = Assert.IsType<ProductResolution.AskOperator>(
            ProductResolver.Resolve(ParsedRequest.Purchase(shared, "credit", 10_000), catalogue, Options));
        Assert.Equal(["zain", "korek"], ask.ProviderCodes);
    }

    [Fact]
    public void NoFitWithNoOperatorIsNotFound()
        => Assert.IsType<ProductResolution.NotFound>(Resolve(null, "credit", 7_000));

    [Fact]
    public void OptionsAreCappedByTheSettingAndOrderedByPrice()
    {
        var options = Assert.IsType<ProductResolution.Options>(Resolve("zain", null, null, new AgentOptions { MaxProductsInOneQuestion = 3 }));
        Assert.Equal(["ZN-CR-5", "ZN-CR-10", "ZN-NET-10"], options.Products.Select(p => p.Code));
    }
}
