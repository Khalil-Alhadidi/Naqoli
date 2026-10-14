using System.Net;
using System.Net.Http.Json;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

[Collection(WalletApiCollection.Name)]
public class QuoteTests(WalletApiFixture fixture)
{
    private const string HammoudiPhone = "07700001004";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PaymentRequest Transfer(int userId, long amount, string? toPhone = null, int? toAccountId = null)
        => new(userId, "transfer", amount, toPhone, toAccountId, null, null, null);

    [Fact]
    public async Task TransferQuoteToHammoudiMatchesTheSlideExample()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync("/quotes", Transfer(SeedData.DemoUserId, 50_000, HammoudiPhone), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<QuoteResponse>(Token);
        Assert.NotNull(quote);
        Assert.Equal("transfer", quote.Type);
        Assert.Equal(50_000, quote.AmountIqd);
        Assert.Equal(1_000, quote.FeeIqd);
        Assert.Equal(51_000, quote.TotalIqd);
        Assert.Equal(300_000, quote.BalanceBeforeIqd);
        Assert.Equal(249_000, quote.BalanceAfterIqd);
        Assert.True(quote.Sufficient);
        Assert.False(quote.PinRequired);
        Assert.Equal(HammoudiPhone, quote.RecipientPhone);
        Assert.Equal("محمود كريم جاسم", quote.RecipientName);

        // A quote moves no money.
        var balance = await fixture.Client.GetFromJsonAsync<BalanceResponse>($"/users/{SeedData.DemoUserId}/balance", Token);
        Assert.Equal(300_000, balance!.BalanceIqd);
    }

    [Fact]
    public async Task QuoteAboveThresholdRequiresPin()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync("/quotes", Transfer(2, 1_500_000, toAccountId: 1), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<QuoteResponse>(Token);
        Assert.NotNull(quote);
        Assert.True(quote.PinRequired);
        Assert.Equal(1_501_000, quote.TotalIqd);

        // Quotes report a shortfall but never refuse on funds; only execute does.
        Assert.False(quote.Sufficient);
        Assert.Equal(1_250_000 - 1_501_000, quote.BalanceAfterIqd);
    }

    [Fact]
    public async Task BillQuoteUsesAmountDueAndRejectsPartial()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var full = await fixture.Client.PostAsJsonAsync(
            "/quotes", new PaymentRequest(SeedData.DemoUserId, "bill", null, null, null, 1, null, null), Token);

        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        var quote = await full.Content.ReadFromJsonAsync<QuoteResponse>(Token);
        Assert.NotNull(quote);
        Assert.Equal("bill", quote.Type);
        Assert.Equal(45_000, quote.AmountIqd);
        Assert.Equal(500, quote.FeeIqd);
        Assert.Equal(45_500, quote.TotalIqd);
        Assert.Equal(1, quote.BillAccountId);

        var partial = await fixture.Client.PostAsJsonAsync(
            "/quotes", new PaymentRequest(SeedData.DemoUserId, "bill", 20_000, null, null, 1, null, null), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, partial.StatusCode);
        var problem = await partial.ReadProblemAsync(Token);
        Assert.Equal("bills_paid_in_full", problem.Code());
        Assert.Equal(45_000, problem.Long("amountDueIqd"));
    }

    [Fact]
    public async Task BillWithNothingDueIs422()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync(
            "/quotes", new PaymentRequest(SeedData.DemoUserId, "bill", null, null, null, 4, null, null), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("nothing_due", (await response.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task PurchaseQuoteUsesCataloguePrice()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync(
            "/quotes", new PaymentRequest(SeedData.DemoUserId, "purchase", null, null, null, null, 1, null), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<QuoteResponse>(Token);
        Assert.NotNull(quote);
        Assert.Equal("purchase", quote.Type);
        Assert.Equal(5_000, quote.AmountIqd);
        Assert.Equal(0, quote.FeeIqd);
        Assert.Equal(5_000, quote.TotalIqd);
        Assert.Equal(295_000, quote.BalanceAfterIqd);
        Assert.Equal(1, quote.ProductId);
    }

    [Fact]
    public async Task UnknownRecipientIs422()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync("/quotes", Transfer(SeedData.DemoUserId, 10_000, "07700009999"), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unknown_recipient", (await response.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task TransferToSelfIs422()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync("/quotes", Transfer(SeedData.DemoUserId, 10_000, "07700001001"), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("same_account", (await response.ReadProblemAsync(Token)).Code());
    }

    [Theory]
    [InlineData(999, "transfer", 10_000L, null, "unknown_user")]
    [InlineData(1, "transfer", 0L, null, "invalid_amount")]
    [InlineData(1, "transfer", null, null, "invalid_amount")]
    [InlineData(1, "purchase", null, 99, "unknown_product")]
    [InlineData(1, "purchase", 4_000L, 1, "invalid_amount")]
    [InlineData(1, "refund", 10_000L, null, "invalid_type")]
    public async Task InvalidRequestsAre422WithACode(int userId, string type, long? amount, int? productId, string code)
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var request = new PaymentRequest(userId, type, amount, HammoudiPhone, null, null, productId, null);
        var response = await fixture.Client.PostAsJsonAsync("/quotes", request, Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(code, (await response.ReadProblemAsync(Token)).Code());
    }
}
