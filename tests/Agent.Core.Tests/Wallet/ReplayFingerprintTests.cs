using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
using Wallet.MockApi.Services;

namespace Agent.Core.Tests.Wallet;

/// <summary>
/// A replay answers only the same caller asking the same thing (milestone 2 review I4), the stored balance after,
/// and seeded keys that no client may use (milestone 2b review M4).
/// </summary>
[Collection(WalletApiCollection.Name)]
public class ReplayFingerprintTests(WalletApiFixture fixture)
{
    private const int DemoUserId = SeedFacts.DemoUserId;
    private const int TightUserId = SeedFacts.TightUserId;
    private const long DemoBalance = SeedFacts.DemoBalance;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PaymentRequest Transfer(int userId, long amount)
        => new(userId, "transfer", amount, SeedFacts.HammoudiPhone, null, null, null, null);

    [Fact]
    public async Task SameKeyDifferentBodyIs422WithoutStoredBody()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var first = await PostAsync("r-body", Transfer(DemoUserId, 50_000));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var reused = await PostAsync("r-body", Transfer(DemoUserId, 60_000));

        await AssertReusedWithoutStoredBodyAsync(reused);
        Assert.Equal(699_000, await BalanceAsync(DemoUserId));
    }

    [Fact]
    public async Task SameKeyDifferentUserIs422()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var first = await PostAsync("r-user", Transfer(DemoUserId, 5_000));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var reused = await PostAsync("r-user", Transfer(TightUserId, 5_000));

        await AssertReusedWithoutStoredBodyAsync(reused);
        Assert.Equal(18_500, await BalanceAsync(TightUserId));
    }

    [Fact]
    public async Task ByKeyNeedsTheOwnersUserId()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var created = await PostAsync("r-owner", Transfer(DemoUserId, 50_000));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var withoutUser = await fixture.Client.GetAsync("/transactions/by-key/r-owner", Token);
        Assert.Equal(HttpStatusCode.BadRequest, withoutUser.StatusCode);
        Assert.Equal("missing_user_id", (await withoutUser.ReadProblemAsync(Token)).Code());

        var otherUser = await fixture.Client.GetAsync($"/transactions/by-key/r-owner?userId={TightUserId}", Token);
        Assert.Equal(HttpStatusCode.NotFound, otherUser.StatusCode);
        Assert.Equal("unknown_key", (await otherUser.ReadProblemAsync(Token)).Code());

        var owner = await fixture.Client.GetAsync($"/transactions/by-key/r-owner?userId={DemoUserId}", Token);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task ReplayReturnsTheStoredBalanceAfter()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var first = await PostAsync("r-balance-1", Transfer(DemoUserId, 50_000));
        var firstBody = await first.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.Equal(699_000, firstBody!.BalanceAfterIqd);

        var second = await PostAsync("r-balance-2", Transfer(DemoUserId, 50_000));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(648_000, await BalanceAsync(DemoUserId));

        // The replay reports the balance right after the first payment, not the balance now.
        var replay = await PostAsync("r-balance-1", Transfer(DemoUserId, 50_000));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody, await replay.Content.ReadFromJsonAsync<TransactionResponse>(Token));

        var lookup = await fixture.Client.GetFromJsonAsync<TransactionResponse>(
            $"/transactions/by-key/r-balance-1?userId={DemoUserId}", Token);
        Assert.Equal(firstBody, lookup);
    }

    [Fact]
    public async Task SeedKeysAreRejected()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var execute = await PostAsync("seed-H0001", Transfer(DemoUserId, 50_000));
        Assert.Equal(HttpStatusCode.BadRequest, execute.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await execute.ReadProblemAsync(Token)).Code());
        Assert.Equal(DemoBalance, await BalanceAsync(DemoUserId));

        var lookup = await fixture.Client.GetAsync($"/transactions/by-key/seed-H0001?userId={DemoUserId}", Token);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal("unknown_key", (await lookup.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task SeededRowsCarryTheFingerprintOfTheirOwnFieldsAndNoBalanceAfter()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.IdempotencyKey.StartsWith("seed-"))
            .Select(t => new
            {
                t.UserId,
                t.Type,
                t.AmountIqd,
                ToPhone = t.ToAccount == null ? null : t.ToAccount.Phone,
                t.BillAccountId,
                t.ProductId,
                t.RequestFingerprint,
                t.BalanceAfterIqd,
            })
            .ToListAsync(Token);

        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.Null(row.BalanceAfterIqd);
            Assert.Equal(
                RequestFingerprint.Of(new PaymentRequest(
                    row.UserId, TransactionNames.Of(row.Type), row.AmountIqd, row.ToPhone, null, row.BillAccountId, row.ProductId, null)),
                row.RequestFingerprint);
        });
    }

    private static async Task AssertReusedWithoutStoredBodyAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var root = json.RootElement;
        Assert.Equal("idempotency_key_reused", root.GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("reference", out _));
        Assert.False(root.TryGetProperty("balanceAfterIqd", out _));
        Assert.False(root.TryGetProperty("transactionId", out _));
    }

    private async Task<HttpResponseMessage> PostAsync(string key, PaymentRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("Idempotency-Key", key);
        return await fixture.Client.SendAsync(message, Token);
    }

    private async Task<long> BalanceAsync(int userId)
    {
        var balance = await fixture.Client.GetFromJsonAsync<BalanceResponse>($"/users/{userId}/balance", Token);
        return balance!.BalanceIqd;
    }
}
