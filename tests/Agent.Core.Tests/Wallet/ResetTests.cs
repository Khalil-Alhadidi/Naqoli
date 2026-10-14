using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

/// <summary><c>POST /admin/reset</c>: the database returns to the seed, identities included, and faults clear.</summary>
[Collection(WalletApiCollection.Name)]
public class ResetTests(WalletApiFixture fixture)
{
    private const string HammoudiPhone = SeedFacts.HammoudiPhone;
    private const int DemoUserId = SeedFacts.DemoUserId;
    private const int DemoAccountId = SeedFacts.DemoAccountId;

    // Any PIN that is not U3's.
    private const string WrongPin = "0000";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResetRestoresTheSeedExactly()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        var before = await SnapshotAsync();

        Assert.Equal(HttpStatusCode.Created, (await PostAsync("r-transfer",
            new PaymentRequest(DemoUserId, "transfer", 50_000, HammoudiPhone, null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("r-bill",
            new PaymentRequest(DemoUserId, "bill", null, null, null, 1, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("r-purchase",
            new PaymentRequest(DemoUserId, "purchase", null, null, null, null, 1, null))).StatusCode);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var wrong = await PostAsync($"r-wrong-{attempt}",
                new PaymentRequest(SeedFacts.TraderUserId, "transfer", 1_100_000, null, DemoAccountId, null, null, WrongPin));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
            Assert.Equal("pin_invalid", (await wrong.ReadProblemAsync(Token)).Code());
        }

        Assert.NotEqual(before, await SnapshotAsync());

        var response = await fixture.Client.PostAsync("/admin/reset", null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.Content.ReadFromJsonAsync<ResetResponse>(Token);

        // Seeded dates follow today's Iraq date, so the two snapshots differ only if the test crosses midnight in Iraq.
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(new ResetResponse(5, 42, 34, 9, 11, 15, 86, 9), counts);

        // Identities start again where the seed left them.
        var next = await PostAsync("r-after",
            new PaymentRequest(DemoUserId, "transfer", 50_000, HammoudiPhone, null, null, null, null));
        var body = await next.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.Equal(87, body!.TransactionId);
    }

    [Fact]
    public async Task ResetClearsFaults()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        var set = await fixture.Client.PostAsJsonAsync("/admin/faults", new FaultRequest("error", null), Token);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var reset = await fixture.Client.PostAsync("/admin/reset", null, Token);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var state = await fixture.Client.GetFromJsonAsync<FaultState>("/admin/faults", Token);
        Assert.Equal("none", state!.Mode);
        var payment = await PostAsync("r-no-fault",
            new PaymentRequest(DemoUserId, "transfer", 50_000, HammoudiPhone, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
    }

    /// <summary>Every table, ordered by Id, as one JSON string. PIN hashes are left out: they are salted per hash.</summary>
    private async Task<string> SnapshotAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        var snapshot = new
        {
            users = await db.Users.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Code, x.Phone, x.DisplayName, x.PinFailedAttempts, x.PinLocked, x.ConsentGiven }).ToListAsync(Token),
            accounts = await db.Accounts.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Code, x.Phone, x.RegisteredName, x.Status, x.UserId, x.IsDefault, x.BalanceIqd }).ToListAsync(Token),
            contacts = await db.Contacts.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Code, x.OwnerUserId, x.SavedName, x.Phone }).ToListAsync(Token),
            aliases = await db.Aliases.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.OwnerUserId, x.Text, x.TargetAccountId }).ToListAsync(Token),
            billers = await db.Billers.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Code, x.Name, x.NameEn }).ToListAsync(Token),
            billAccounts = await db.BillAccounts.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Code, x.UserId, x.BillerId, x.Label, x.AccountNumber, x.AmountDueIqd }).ToListAsync(Token),
            products = await db.Products.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.Id, x.Code, x.Provider, x.ProviderNameEn, x.ProviderNameAr, x.AlsoCalled, x.Type, x.FaceValueIqd, x.PriceIqd,
                })
                .ToListAsync(Token),
            transactions = await db.Transactions.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.Id, x.IdempotencyKey, x.Reference, x.UserId, x.FromAccountId, x.Type, x.Status, x.ErrorCode,
                    x.AmountIqd, x.FeeIqd, x.TotalIqd, x.ToAccountId, x.BillAccountId, x.ProductId, x.CreatedAt, x.CompletedAt,
                })
                .ToListAsync(Token),
            orders = await db.Orders.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.UserId, x.ProductId, x.TransactionId, x.Code, x.CreatedAt }).ToListAsync(Token),
        };

        return JsonSerializer.Serialize(snapshot);
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
}
