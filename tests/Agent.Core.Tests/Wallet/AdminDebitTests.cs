using System.Net;
using System.Net.Http.Json;
using Wallet.MockApi.Contracts;

namespace Agent.Core.Tests.Wallet;

/// <summary>
/// <c>POST /admin/debit</c> (Khalil, 14 Oct 2026): money leaves a user's wallet from outside the agent. Admin key, the
/// gate held exclusive, one conditional debit that never goes below zero, a <c>done</c> history row of type
/// <c>externalDebit</c> with fee 0; a reset restores the seed.
/// </summary>
[Collection(WalletApiCollection.Name)]
public class AdminDebitTests(WalletApiFixture fixture)
{
    private const int DemoUserId = SeedFacts.DemoUserId;
    private const long DemoSeedBalance = SeedFacts.DemoBalance;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task StartAsync()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
    }

    private Task<HttpResponseMessage> DebitAsync(int userId, long amountIqd, HttpClient? client = null)
        => (client ?? fixture.Client).PostAsJsonAsync("/admin/debit", new DebitRequest(userId, amountIqd), Token);

    private async Task<long> BalanceAsync(int userId)
        => (await fixture.Client.GetFromJsonAsync<BalanceResponse>($"/users/{userId}/balance", Token))!.BalanceIqd;

    private async Task<List<HistoryItem>> HistoryAsync(int userId)
        => (await fixture.Client.GetFromJsonAsync<List<HistoryItem>>($"/users/{userId}/history?take=1000", Token))!;

    [Fact]
    public async Task DebitLowersTheBalanceAndWritesAnExternalDebitRow()
    {
        await StartAsync();

        var response = await DebitAsync(DemoUserId, 5_000);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var debit = await response.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.NotNull(debit);
        Assert.Equal(
            ("externalDebit", "done", 5_000L, 0L, 5_000L, DemoSeedBalance - 5_000),
            (debit.Type, debit.Status, debit.AmountIqd, debit.FeeIqd, debit.TotalIqd, debit.BalanceAfterIqd));
        Assert.StartsWith("ZC-", debit.Reference, StringComparison.Ordinal);
        Assert.Equal(DemoSeedBalance - 5_000, await BalanceAsync(DemoUserId));

        var row = Assert.Single(await HistoryAsync(DemoUserId), h => h.TransactionId == debit.TransactionId);
        Assert.Equal(("externalDebit", "done", 5_000L, 0L), (row.Type, row.Status, row.AmountIqd, row.FeeIqd));
        Assert.Null(row.CounterpartyPhone);
    }

    [Fact]
    public async Task DebitBelowZeroIsRefusedAndNothingChanges()
    {
        await StartAsync();

        var response = await DebitAsync(DemoUserId, DemoSeedBalance + 1);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("insufficient_funds", (await response.ReadProblemAsync(Token)).Code());
        Assert.Equal(DemoSeedBalance, await BalanceAsync(DemoUserId));
        Assert.DoesNotContain(await HistoryAsync(DemoUserId), h => h.Type == "externalDebit");

        // The whole balance may go: zero is not below zero.
        Assert.Equal(HttpStatusCode.Created, (await DebitAsync(DemoUserId, DemoSeedBalance)).StatusCode);
        Assert.Equal(0, await BalanceAsync(DemoUserId));
    }

    [Fact]
    public async Task DebitNeedsTheAdminKey()
    {
        await StartAsync();
        using var bare = fixture.CreateClient();

        var response = await DebitAsync(DemoUserId, 5_000, bare);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("admin_key_required", (await response.ReadProblemAsync(Token)).Code());
        Assert.Equal(DemoSeedBalance, await BalanceAsync(DemoUserId));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5_000L)]
    public async Task DebitOfZeroOrNegativeIsRefused(long amountIqd)
    {
        await StartAsync();

        var response = await DebitAsync(DemoUserId, amountIqd);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invalid_amount", (await response.ReadProblemAsync(Token)).Code());
        Assert.Equal(DemoSeedBalance, await BalanceAsync(DemoUserId));
    }

    [Fact]
    public async Task DebitOfAnUnknownUserIsRefused()
    {
        await StartAsync();

        var response = await DebitAsync(999, 5_000);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unknown_user", (await response.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task TheDebitsKeyCanNeitherBeReplayedNorLookedUp()
    {
        await StartAsync();
        var debit = await (await DebitAsync(DemoUserId, 5_000)).Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.NotNull(debit);

        // The row's key is admin-<guid>; any admin- key is refused on execute and unknown on by-key, like seed- keys.
        using var execute = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new PaymentRequest(DemoUserId, "transfer", 1_000, SeedFacts.HammoudiPhone, null, null, null, null)),
        };
        execute.Headers.Add("Idempotency-Key", "admin-0123456789abcdef");
        var refused = await fixture.Client.SendAsync(execute, Token);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await refused.ReadProblemAsync(Token)).Code());

        var lookup = await fixture.Client.GetAsync($"/transactions/by-key/admin-0123456789abcdef?userId={DemoUserId}", Token);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal("unknown_key", (await lookup.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task ResetRemovesTheDebit()
    {
        await StartAsync();
        Assert.Equal(HttpStatusCode.Created, (await DebitAsync(DemoUserId, 5_000)).StatusCode);

        await fixture.ResetAsync();

        Assert.Equal(DemoSeedBalance, await BalanceAsync(DemoUserId));
        Assert.DoesNotContain(await HistoryAsync(DemoUserId), h => h.Type == "externalDebit");
    }
}
