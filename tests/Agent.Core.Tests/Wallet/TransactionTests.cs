using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;

namespace Agent.Core.Tests.Wallet;

[Collection(WalletApiCollection.Name)]
public class TransactionTests(WalletApiFixture fixture)
{
    private const string HammoudiPhone = "07700001004";
    private const int HammoudiId = 4;

    // The demo user's default account. In the seed, account ids equal user ids.
    private const int DemoAccountId = 1;
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

        var blank = await PostAsync("   ", SendFiftyThousandToHammoudi);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("missing_idempotency_key", (await blank.ReadProblemAsync(Token)).Code());

        Assert.Equal(300_000, await BalanceAsync(SeedData.DemoUserId));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk")]
    [InlineData("dot.key")]
    public async Task MalformedKeyIs400(string key)
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();

        var response = await PostAsync(key, SendFiftyThousandToHammoudi);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await response.ReadProblemAsync(Token)).Code());
        Assert.Equal(300_000, await BalanceAsync(SeedData.DemoUserId));

        // By-key applies the same format: a malformed key is simply unknown.
        var lookup = await fixture.Client.GetAsync($"/transactions/by-key/{Uri.EscapeDataString(key)}", Token);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal("unknown_key", (await lookup.ReadProblemAsync(Token)).Code());
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

        var request = Transfer(3, 1_000, toAccountId: DemoAccountId);
        var response = await PostAsync("t-empty", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("insufficient_funds", (await response.ReadProblemAsync(Token)).Code());
        Assert.Equal(0, await BalanceAsync(3));
        Assert.Equal(300_000, await BalanceAsync(SeedData.DemoUserId));

        // The failure is stored against the key, and a retry gets the same answer without executing.
        var lookup = await fixture.Client.GetFromJsonAsync<TransactionResponse>("/transactions/by-key/t-empty", Token);
        Assert.Equal("failed", lookup!.Status);
        Assert.Equal("insufficient_funds", lookup.ErrorCode);

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
    public async Task TwoConcurrentPaymentsOfOneBillPayOnce()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        for (var round = 0; round < ConcurrencyRounds; round++)
        {
            await fixture.ResetAsync();

            // Bill account 1 has 45,000 due. Two payments with different keys race; the bill is cleared once.
            var request = new PaymentRequest(SeedData.DemoUserId, "bill", null, null, null, 1, null, null);
            var responses = await Task.WhenAll(
                PostAsync($"bill-a-{round}", request),
                PostAsync($"bill-b-{round}", request));

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            var refused = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
            Assert.Equal("nothing_due", (await refused.ReadProblemAsync(Token)).Code());

            Assert.Equal(300_000 - 45_000 - 500, await BalanceAsync(SeedData.DemoUserId));
            var bills = await fixture.Client.GetFromJsonAsync<List<BillItem>>($"/users/{SeedData.DemoUserId}/bills", Token);
            Assert.Equal(0, Assert.Single(bills!, b => b.BillAccountId == 1).AmountDueIqd);
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
        var withoutPin = await PostAsync("t-pin-none", Transfer(2, 1_100_000, toAccountId: DemoAccountId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutPin.StatusCode);
        Assert.Equal("pin_required", (await withoutPin.ReadProblemAsync(Token)).Code());
        Assert.Equal(1_250_000, await BalanceAsync(2));

        var withPin = await PostAsync("t-pin-ok", Transfer(2, 1_100_000, toAccountId: DemoAccountId, pin: SeedPin));
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
            var wrong = await PostAsync($"t-wrong-{attempt}", Transfer(2, 1_100_000, toAccountId: DemoAccountId, pin: "9999"));

            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
            var problem = await wrong.ReadProblemAsync(Token);
            Assert.Equal("pin_invalid", problem.Code());
            Assert.Equal(3 - attempt, problem.Long("attemptsLeft"));
        }

        // Once locked, even the right PIN is refused, and nothing moves.
        var locked = await PostAsync("t-locked", Transfer(2, 1_100_000, toAccountId: DemoAccountId, pin: SeedPin));
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        Assert.Equal("pin_locked", (await locked.ReadProblemAsync(Token)).Code());
        Assert.Equal(1_250_000, await BalanceAsync(2));

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var sara = await db.Users.AsNoTracking().SingleAsync(u => u.Id == 2, Token);
        Assert.True(sara.PinLocked);
        Assert.Equal(3, sara.PinFailedAttempts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ConcurrentWrongPinsCannotExceedTheLockout(int priorWrongPins)
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        const int maxPinAttempts = 3;
        const int wrongGuesses = 20;
        const long saraSeedBalance = 1_250_000;
        const long saraAfterOnePayment = saraSeedBalance - 1_100_000 - 1_000;
        var slotsLeft = maxPinAttempts - priorWrongPins;

        for (var round = 0; round < ConcurrencyRounds; round++)
        {
            await fixture.ResetAsync();

            // Wrong PINs entered one at a time first, so the race starts with fewer slots left.
            for (var prior = 0; prior < priorWrongPins; prior++)
            {
                var wrong = await PostAsync($"pin-race-{round}-prior-{prior}", Transfer(2, 1_100_000, toAccountId: DemoAccountId, pin: "9999"));
                Assert.Equal("pin_invalid", (await wrong.ReadProblemAsync(Token)).Code());
            }

            // Sara (user 2) sends 1,100,000, above the PIN threshold: 20 wrong PINs and the right one, all at once.
            var calls = Enumerable.Range(0, wrongGuesses)
                .Select(i => PostAsync($"pin-race-{round}-wrong-{i}", Transfer(2, 1_100_000, toAccountId: DemoAccountId, pin: "9999")))
                .Append(PostAsync($"pin-race-{round}-right", Transfer(2, 1_100_000, toAccountId: DemoAccountId, pin: SeedPin)))
                .ToList();
            var responses = await Task.WhenAll(calls);

            var attemptsLeft = new List<long>();
            foreach (var response in responses.Take(wrongGuesses))
            {
                var problem = await response.ReadProblemAsync(Token);
                if (response.StatusCode == HttpStatusCode.Locked)
                {
                    Assert.Equal("pin_locked", problem.Code());
                    continue;
                }

                Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
                Assert.Equal("pin_invalid", problem.Code());
                attemptsLeft.Add(problem.Long("attemptsLeft"));
            }

            // The right PIN either won a reserved slot and paid, or found the PIN locked. Nothing else.
            var rightWon = responses[wrongGuesses].StatusCode == HttpStatusCode.Created;
            if (!rightWon)
                Assert.Equal(HttpStatusCode.Locked, responses[wrongGuesses].StatusCode);

            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
            var sara = await db.Users.AsNoTracking().SingleAsync(u => u.Id == 2, Token);
            var report = $"attemptsLeft [{string.Join(", ", attemptsLeft.Order())}], the right PIN {(rightWon ? "paid" : "was locked out")}, "
                + $"PinFailedAttempts {sara.PinFailedAttempts}, PinLocked {sara.PinLocked}";

            // Never more counted attempts than the limit.
            Assert.True(sara.PinFailedAttempts <= maxPinAttempts, report);

            if (!rightWon)
            {
                // Locked out: the wrong PINs checked are exactly the slots that were left, each attempt number once,
                // and the PIN is locked.
                Assert.True(attemptsLeft.Order().SequenceEqual(Enumerable.Range(0, slotsLeft).Select(n => (long)n)), report);
                Assert.True(sara.PinLocked, report);
            }
            else if (slotsLeft == 1)
            {
                // The right PIN paid only by holding the last slot: no wrong PIN was checked before it, it cleared its
                // own lock, and any wrong PIN checked after it counts again from the top (2, then 1, then 0 left).
                var expected = Enumerable.Range(1, attemptsLeft.Count).Select(n => (long)(maxPinAttempts - n));
                Assert.True(attemptsLeft.OrderDescending().SequenceEqual(expected), report);
            }
            else
            {
                // It held one of the slots: fewer than the slots left were checked before it, at most the limit after it.
                Assert.True(attemptsLeft.Count <= slotsLeft - 1 + maxPinAttempts, report);
            }

            if (sara.PinLocked)
            {
                // Locked: even the right PIN is refused now.
                var final = await PostAsync($"pin-race-{round}-final", Transfer(2, 1_100_000, toAccountId: DemoAccountId, pin: SeedPin));
                Assert.Equal(HttpStatusCode.Locked, final.StatusCode);
                Assert.Equal("pin_locked", (await final.ReadProblemAsync(Token)).Code());
            }
            else
            {
                // Only a right PIN unlocks: here it held the last slot and the wrong PINs that came while it was checked
                // were refused unchecked (423).
                Assert.True(rightWon, report);
            }

            // Money moved at most once, and only if the right PIN won.
            var keyPrefix = $"pin-race-{round}-";
            var done = await db.Transactions.CountAsync(
                t => t.IdempotencyKey.StartsWith(keyPrefix) && t.Status == TransactionStatus.Done, Token);
            Assert.Equal(rightWon ? 1 : 0, done);
            Assert.Equal(rightWon ? saraAfterOnePayment : saraSeedBalance, await BalanceAsync(2));
        }
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
