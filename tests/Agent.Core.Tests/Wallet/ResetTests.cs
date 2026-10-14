using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

/// <summary><c>POST /admin/reset</c>: the database returns to the frozen seed, identities included, and faults clear.</summary>
[Collection(WalletApiCollection.Name)]
public class ResetTests(WalletApiFixture fixture)
{
    private const string HammoudiPhone = "07700001004";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResetRestoresTheSeedExactly()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        var before = await SnapshotAsync();

        Assert.Equal(HttpStatusCode.Created, (await PostAsync("r-transfer",
            new PaymentRequest(SeedData.DemoUserId, "transfer", 50_000, HammoudiPhone, null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("r-bill",
            new PaymentRequest(SeedData.DemoUserId, "bill", null, null, null, 1, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("r-purchase",
            new PaymentRequest(SeedData.DemoUserId, "purchase", null, null, null, null, 1, null))).StatusCode);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var wrong = await PostAsync($"r-wrong-{attempt}",
                new PaymentRequest(2, "transfer", 1_100_000, null, SeedData.DemoUserId, null, null, "9999"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        }

        Assert.NotEqual(before, await SnapshotAsync());

        var response = await fixture.Client.PostAsync("/admin/reset", null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.Content.ReadFromJsonAsync<ResetResponse>(Token);

        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(
            new ResetResponse(
                SeedData.Users().Count,
                SeedData.Users().Sum(u => u.Accounts.Count),
                SeedData.Users().Sum(u => u.Contacts.Count),
                SeedData.Users().Sum(u => u.Aliases.Count),
                SeedData.Users().Sum(u => u.BillAccounts.Count),
                SeedData.Products().Count,
                SeedData.Transactions().Count,
                SeedData.Orders().Count),
            counts);

        // Identities start again where the seed left them.
        var next = await PostAsync("r-after",
            new PaymentRequest(SeedData.DemoUserId, "transfer", 50_000, HammoudiPhone, null, null, null, null));
        var body = await next.Content.ReadFromJsonAsync<TransactionResponse>(Token);
        Assert.Equal(SeedData.Transactions().Count + 1, body!.TransactionId);

        var committed = File.ReadAllText(Path.Combine(SeedHashTests.RepositoryRoot(), "data", "seed", "seed-hash.txt")).Trim();
        Assert.Equal(committed, SeedData.ComputeHash());
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
            new PaymentRequest(SeedData.DemoUserId, "transfer", 50_000, HammoudiPhone, null, null, null, null));
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
                .Select(x => new { x.Id, x.Phone, x.DisplayName, x.PinFailedAttempts, x.PinLocked }).ToListAsync(Token),
            accounts = await db.Accounts.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.UserId, x.IsDefault, x.BalanceIqd }).ToListAsync(Token),
            contacts = await db.Contacts.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.OwnerUserId, x.SavedName, x.Phone }).ToListAsync(Token),
            aliases = await db.Aliases.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.OwnerUserId, x.Text, x.TargetAccountId }).ToListAsync(Token),
            billers = await db.Billers.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(Token),
            billAccounts = await db.BillAccounts.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.UserId, x.BillerId, x.Label, x.AccountNumber, x.AmountDueIqd, x.DueDate }).ToListAsync(Token),
            products = await db.Products.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Provider, x.Type, x.Name, x.FaceValueIqd, x.PriceIqd }).ToListAsync(Token),
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
