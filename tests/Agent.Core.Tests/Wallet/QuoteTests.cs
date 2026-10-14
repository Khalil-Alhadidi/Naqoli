using System.Net;
using System.Net.Http.Json;
using Wallet.MockApi.Contracts;

namespace Agent.Core.Tests.Wallet;

[Collection(WalletApiCollection.Name)]
public class QuoteTests(WalletApiFixture fixture)
{
    private const string HammoudiPhone = SeedFacts.HammoudiPhone;
    private const int DemoUserId = SeedFacts.DemoUserId;
    private const long DemoBalance = SeedFacts.DemoBalance;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PaymentRequest Transfer(int userId, long amount, string? toPhone = null, int? toAccountId = null)
        => new(userId, "transfer", amount, toPhone, toAccountId, null, null, null);

    [Fact]
    public async Task TransferQuoteToHammoudiMatchesTheSlideExample()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync("/quotes", Transfer(DemoUserId, 50_000, HammoudiPhone), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<QuoteResponse>(Token);
        Assert.NotNull(quote);
        Assert.Equal("transfer", quote.Type);
        Assert.Equal(50_000, quote.AmountIqd);
        Assert.Equal(1_000, quote.FeeIqd);
        Assert.Equal(51_000, quote.TotalIqd);
        Assert.Equal(DemoBalance, quote.BalanceBeforeIqd);
        Assert.Equal(699_000, quote.BalanceAfterIqd);
        Assert.True(quote.Sufficient);
        Assert.False(quote.PinRequired);
        Assert.Equal(HammoudiPhone, quote.RecipientPhone);
        Assert.Equal(SeedFacts.Account(HammoudiPhone).RegisteredName, quote.RecipientName);

        // A quote moves no money.
        var balance = await fixture.Client.GetFromJsonAsync<BalanceResponse>($"/users/{DemoUserId}/balance", Token);
        Assert.Equal(DemoBalance, balance!.BalanceIqd);
    }

    [Fact]
    public async Task QuoteAboveThresholdRequiresPin()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync("/quotes", Transfer(SeedFacts.TightUserId, 1_500_000, toAccountId: SeedFacts.DemoAccountId), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<QuoteResponse>(Token);
        Assert.NotNull(quote);
        Assert.True(quote.PinRequired);
        Assert.Equal(1_501_000, quote.TotalIqd);

        // Quotes report a shortfall but never refuse on funds; only execute does.
        Assert.False(quote.Sufficient);
        Assert.Equal(18_500 - 1_501_000, quote.BalanceAfterIqd);
    }

    [Fact]
    public async Task BillQuoteUsesAmountDueAndRejectsPartial()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var full = await fixture.Client.PostAsJsonAsync(
            "/quotes", new PaymentRequest(DemoUserId, "bill", null, null, null, 1, null, null), Token);

        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        var quote = await full.Content.ReadFromJsonAsync<QuoteResponse>(Token);
        Assert.NotNull(quote);
        Assert.Equal("bill", quote.Type);
        Assert.Equal(43_750, quote.AmountIqd);
        Assert.Equal(500, quote.FeeIqd);
        Assert.Equal(44_250, quote.TotalIqd);
        Assert.Equal(1, quote.BillAccountId);

        var partial = await fixture.Client.PostAsJsonAsync(
            "/quotes", new PaymentRequest(DemoUserId, "bill", 20_000, null, null, 1, null, null), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, partial.StatusCode);
        var problem = await partial.ReadProblemAsync(Token);
        Assert.Equal("bills_paid_in_full", problem.Code());
        Assert.Equal(43_750, problem.Long("amountDueIqd"));
    }

    [Fact]
    public async Task BillWithNothingDueIs422()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        // U2's bill account 5 (BZ1) has nothing due.
        var response = await fixture.Client.PostAsJsonAsync(
            "/quotes", new PaymentRequest(SeedFacts.TightUserId, "bill", null, null, null, 5, null, null), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("nothing_due", (await response.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task PurchaseQuoteUsesCataloguePrice()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync(
            "/quotes", new PaymentRequest(DemoUserId, "purchase", null, null, null, null, 1, null), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<QuoteResponse>(Token);
        Assert.NotNull(quote);
        Assert.Equal("purchase", quote.Type);
        Assert.Equal(5_000, quote.AmountIqd);
        Assert.Equal(0, quote.FeeIqd);
        Assert.Equal(5_000, quote.TotalIqd);
        Assert.Equal(745_000, quote.BalanceAfterIqd);
        Assert.Equal(1, quote.ProductId);
    }

    [Fact]
    public async Task UnknownRecipientIs422()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync("/quotes", Transfer(DemoUserId, 10_000, "07805559999"), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unknown_recipient", (await response.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task QuoteToClosedAccountIsRefused()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        // S20, U1's contact whose account is closed; X04, a closed account with no registered name.
        foreach (var phone in new[] { "07805550120", "07805550193" })
        {
            var byPhone = await fixture.Client.PostAsJsonAsync("/quotes", Transfer(DemoUserId, 10_000, phone), Token);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, byPhone.StatusCode);
            var problem = await byPhone.ReadProblemAsync(Token);
            Assert.Equal("account_closed", problem.Code());
            Assert.Equal("Recipient account is closed", problem.Title);

            // Naming the account by id is refused the same way.
            var account = await fixture.Client.GetFromJsonAsync<AccountSummary>($"/accounts/by-phone/{phone}", Token);
            var byId = await fixture.Client.PostAsJsonAsync(
                "/quotes", Transfer(DemoUserId, 10_000, toAccountId: account!.AccountId), Token);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, byId.StatusCode);
            Assert.Equal("account_closed", (await byId.ReadProblemAsync(Token)).Code());
        }
    }

    [Fact]
    public async Task TransferToSelfIs422()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.PostAsJsonAsync("/quotes", Transfer(DemoUserId, 10_000, SeedFacts.DemoPhone), Token);

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
