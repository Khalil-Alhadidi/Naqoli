using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

[Collection(WalletApiCollection.Name)]
public class TransactionTests(WalletApiFixture fixture)
{
    private const string HammoudiPhone = "07700001004";
    private const int HammoudiId = 4;
    private const string SeedPin = "1234";
    private const int ConcurrencyRounds = 3;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PaymentRequest Transfer(int userId, long amount, string? toPhone = null, int? toAccountId = null, string? pin = null)
        => new(userId, "transfer", amount, toPhone, toAccountId, null, null, pin);

    private static PaymentRequest SendFiftyThousandToHammoudi => Transfer(SeedData.DemoUserId, 50_000, HammoudiPhone);

    [Fact]
    public async Task MissingKeyIs400()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var missing = await fixture.Client.PostAsJsonAsync("/transactions", SendFiftyThousandToHammoudi, Token);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("missing_idempotency_key", (await missing.ReadProblemAsync(Token)).Code());

        var tooLong = await PostAsync(new string('k', 65), SendFiftyThousandToHammoudi);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("missing_idempotency_key", (await tooLong.ReadProblemAsync(Token)).Code());

        Assert.Equal(300_000, await BalanceAsync(SeedData.DemoUserId));
    }

    [Fact]
    public async Task TransferMovesMoneyAndCreditsRecipient()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await PostAsync("t-transfer-1", SendFiftyThousandToHammoudi);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/transactions/by-key/t-transfer-1", response.Headers.Location?.OriginalString);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.NotNull(body);
        Assert.Equal(20, body.TransactionId);
        Assert.Equal("ZC-0000000020", body.Reference);
        Assert.Equal("transfer", body.Type);
        Assert.Equal("done", body.Status);
        Assert.Equal(50_000, body.AmountIqd);
        Assert.Equal(1_000, body.FeeIqd);
        Assert.Equal(51_000, body.TotalIqd);
        Assert.Equal(249_000, body.BalanceAfterIqd);
        Assert.Null(body.Code);

        Assert.Equal(249_000, await BalanceAsync(SeedData.DemoUserId));
        Assert.Equal(130_000, await BalanceAsync(HammoudiId));
    }

    [Fact]
    public async Task SameKeyTwiceExecutesOnce()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var first = await PostAsync("t-same-key", SendFiftyThousandToHammoudi);
        var second = await PostAsync("t-same-key", SendFiftyThousandToHammoudi);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        var secondBody = await second.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.Equal(firstBody, secondBody);

        Assert.Equal(249_000, await BalanceAsync(SeedData.DemoUserId));
        Assert.Equal(130_000, await BalanceAsync(HammoudiId));
        Assert.Equal(1, await RowsWithKeyAsync("t-same-key"));
    }

    [Fact]
    public async Task TwoConcurrentCallsWithTheSameKeyExecuteOnce()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        for (var round = 0; round < ConcurrencyRounds; round++)
        {
            await fixture.ResetAsync();
            var key = $"t-concurrent-{round}";

            var responses = await Task.WhenAll(
                PostAsync(key, SendFiftyThousandToHammoudi),
                PostAsync(key, SendFiftyThousandToHammoudi));

            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Single(statuses, HttpStatusCode.Created);
            Assert.Single(statuses, s => s is HttpStatusCode.OK or HttpStatusCode.Conflict);

            Assert.Equal(249_000, await BalanceAsync(SeedData.DemoUserId));
            Assert.Equal(130_000, await BalanceAsync(HammoudiId));
            Assert.Equal(1, await RowsWithKeyAsync(key));
        }
    }

    [Fact]
    public async Task InsufficientFundsIs422AndNothingMoves()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var request = Transfer(3, 1_000, toAccountId: SeedData.DemoUserId);
        var response = await PostAsync("t-empty", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("insufficient_funds", (await response.ReadProblemAsync(Token)).Code());
        Assert.Equal(0, await BalanceAsync(3));
        Assert.Equal(300_000, await BalanceAsync(SeedData.DemoUserId));

        // The failure is stored against the key, and a retry gets the same answer without executing.
        var lookup = await fixture.Client.GetFromJsonAsync<TransactionResponse>("/transactions/by-key/t-empty", Token);
        Assert.Equal("failed", lookup!.Status);

        var replay = await PostAsync("t-empty", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, replay.StatusCode);
        Assert.Equal("insufficient_funds", (await replay.ReadProblemAsync(Token)).Code());
        Assert.Equal(1, await RowsWithKeyAsync("t-empty"));
    }

    [Fact]
    public async Task TwoConcurrentAffordableTransfersNeverOverdraw()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        for (var round = 0; round < ConcurrencyRounds; round++)
        {
            await fixture.ResetAsync();

            // User 7 holds 10,000. Each transfer costs 6,000 plus the 1,000 fee: one fits, two do not.
            var request = Transfer(7, 6_000, toAccountId: 8);
            var responses = await Task.WhenAll(
                PostAsync($"t-overdraw-a-{round}", request),
                PostAsync($"t-overdraw-b-{round}", request));

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            var refused = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
            Assert.Equal("insufficient_funds", (await refused.ReadProblemAsync(Token)).Code());

            Assert.Equal(3_000, await BalanceAsync(7));
            Assert.Equal(16_000, await BalanceAsync(8));
        }
    }

    [Fact]
    public async Task BillPaymentZeroesTheAmountDue()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var request = new PaymentRequest(SeedData.DemoUserId, "bill", null, null, null, 1, null, null);
        var response = await PostAsync("t-bill", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.NotNull(body);
        Assert.Equal("bill", body.Type);
        Assert.Equal(45_000, body.AmountIqd);
        Assert.Equal(45_500, body.TotalIqd);
        Assert.Equal(254_500, body.BalanceAfterIqd);

        var bills = await fixture.Client.GetFromJsonAsync<List<BillItem>>($"/users/{SeedData.DemoUserId}/bills", Token);
        Assert.Equal(0, Assert.Single(bills!, b => b.BillAccountId == 1).AmountDueIqd);

        // A retry after the bill is paid still returns the stored result, not "nothing due".
        var replay = await PostAsync("t-bill", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(body, await replay.Content.ReadFromJsonAsync<TransactionResponse>(Token));
        Assert.Equal(254_500, await BalanceAsync(SeedData.DemoUserId));
    }

    [Fact]
    public async Task PurchaseReturnsACodeAndCreatesAnOrder()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var request = new PaymentRequest(SeedData.DemoUserId, "purchase", null, null, null, null, 1, null);
        var response = await PostAsync("t-purchase", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.NotNull(body);
        Assert.Equal("purchase", body.Type);
        Assert.Equal(5_000, body.TotalIqd);
        Assert.Equal(295_000, body.BalanceAfterIqd);
        Assert.NotNull(body.Code);
        Assert.Matches("^[0-9]{13}$", body.Code);

        var orders = await fixture.Client.GetFromJsonAsync<List<OrderItem>>($"/users/{SeedData.DemoUserId}/orders", Token);
        var order = Assert.Single(orders!, o => o.TransactionId == body.TransactionId);
        Assert.Equal(body.Code, order.Code);
        Assert.Equal(1, order.ProductId);

        var replay = await PostAsync("t-purchase", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(body.Code, (await replay.Content.ReadFromJsonAsync<TransactionResponse>(Token))!.Code);
    }

    [Fact]
    public async Task PinRequiredAboveThreshold()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        // Sara holds 1,250,000; 1,100,000 plus the fee is above the 1,000,000 PIN threshold.
        var withoutPin = await PostAsync("t-pin-none", Transfer(2, 1_100_000, toAccountId: SeedData.DemoUserId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutPin.StatusCode);
        Assert.Equal("pin_required", (await withoutPin.ReadProblemAsync(Token)).Code());
        Assert.Equal(1_250_000, await BalanceAsync(2));

        var withPin = await PostAsync("t-pin-ok", Transfer(2, 1_100_000, toAccountId: SeedData.DemoUserId, pin: SeedPin));
        Assert.Equal(HttpStatusCode.Created, withPin.StatusCode);
        Assert.Equal(149_000, await BalanceAsync(2));
        Assert.Equal(1_400_000, await BalanceAsync(SeedData.DemoUserId));
    }

    [Fact]
    public async Task ThreeWrongPinsLockTheUser()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var wrong = await PostAsync($"t-wrong-{attempt}", Transfer(2, 1_100_000, toAccountId: SeedData.DemoUserId, pin: "9999"));

            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
            var problem = await wrong.ReadProblemAsync(Token);
            Assert.Equal("pin_invalid", problem.Code());
            Assert.Equal(3 - attempt, problem.Long("attemptsLeft"));
        }

        // Once locked, even the right PIN is refused, and nothing moves.
        var locked = await PostAsync("t-locked", Transfer(2, 1_100_000, toAccountId: SeedData.DemoUserId, pin: SeedPin));
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        Assert.Equal("pin_locked", (await locked.ReadProblemAsync(Token)).Code());
        Assert.Equal(1_250_000, await BalanceAsync(2));

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var sara = await db.Users.AsNoTracking().SingleAsync(u => u.Id == 2, Token);
        Assert.True(sara.PinLocked);
        Assert.Equal(3, sara.PinFailedAttempts);
    }

    [Fact]
    public async Task ByKeyFindsADoneTransaction()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var created = await PostAsync("t-lookup", SendFiftyThousandToHammoudi);
        var createdBody = await created.Content.ReadFromJsonAsync<TransactionResponse>(Token);

        var lookup = await fixture.Client.GetAsync("/transactions/by-key/t-lookup", Token);

        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        var body = await lookup.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.Equal(createdBody, body);
        Assert.Equal("done", body!.Status);
    }

    [Fact]
    public async Task ByKeyUnknownIs404()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await fixture.Client.GetAsync("/transactions/by-key/no-such-key", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown_key", (await response.ReadProblemAsync(Token)).Code());
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

    private async Task<int> RowsWithKeyAsync(string key)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Transactions.CountAsync(t => t.IdempotencyKey == key, Token);
    }
}
