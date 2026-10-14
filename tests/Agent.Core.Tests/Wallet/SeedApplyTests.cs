using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;
using Wallet.MockApi.Data.Seed;
using Wallet.MockApi.Services;

namespace Agent.Core.Tests.Wallet;

/// <summary><see cref="SeedData.Apply"/> and <see cref="SeedReset.ResetAsync"/> against the test database.</summary>
[Collection(WalletApiCollection.Name)]
public class SeedApplyTests(WalletApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApplyTwiceDoesNotDuplicateRows()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var (seed, now) = SeedAndNow(scope);

        SeedData.Apply(db, seed, now);
        SeedData.Apply(db, seed, now);

        Assert.Equal(5, await db.Users.CountAsync(Token));
        Assert.Equal(42, await db.Accounts.CountAsync(Token));
        Assert.Equal(86, await db.Transactions.CountAsync(Token));
        Assert.Equal(seed.Contacts.Count, await db.Contacts.CountAsync(Token));
        Assert.Equal(seed.Bills.Count, await db.BillAccounts.CountAsync(Token));
        Assert.Equal(seed.Products.Count, await db.Products.CountAsync(Token));
    }

    [Fact]
    public async Task ResetRestoresIdentitiesFromOne()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var (seed, now) = SeedAndNow(scope);
        try
        {
            await db.Accounts.Where(a => a.UserId == SeedFacts.DemoUserId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.BalanceIqd, 1L), Token);
            await db.Contacts.Where(c => c.OwnerUserId == SeedFacts.DemoUserId).ExecuteDeleteAsync(Token);

            await SeedReset.ResetAsync(db, seed, now, Token);
            db.ChangeTracker.Clear();

            var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(Token);
            Assert.Equal([1, 2, 3, 4, 5], users.Select(u => u.Id));
            Assert.Equal(["U1", "U2", "U3", "U4", "U5"], users.Select(u => u.Code));
            Assert.Equal(seed.Users.Select(u => u.ConsentGiven), users.Select(u => u.ConsentGiven));

            // No PIN is stored in clear: each user holds a hash of its own seed PIN.
            Assert.All(users, u =>
            {
                Assert.StartsWith("pbkdf2$", u.PinHash);
                Assert.True(PinHasher.Verify(SeedFacts.PinOf(u.Code), u.PinHash), u.Code);
            });

            // Accounts 1 to 5 are the users' own, one each; the other 37 belong to no user.
            var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Token);
            Assert.Equal(Enumerable.Range(1, 42), accounts.Select(a => a.Id));
            Assert.Equal([1, 2, 3, 4, 5], accounts.Take(5).Select(a => a.UserId!.Value));
            Assert.All(accounts.Take(5), a => Assert.True(a.IsDefault));
            Assert.All(accounts.Skip(5), a => Assert.Null(a.UserId));
            Assert.Equal(SeedFacts.DemoPhone, accounts[0].Phone);
            Assert.Equal(SeedFacts.DemoBalance, accounts[0].BalanceIqd);
            Assert.Equal(["X01", "X02", "X03", "X04"], accounts.Where(a => a.Code is not null).Select(a => a.Code!).Order());

            Assert.Equal(1, await db.Contacts.Where(c => c.Code == "S01").Select(c => c.Id).SingleAsync(Token));
            Assert.Equal(34, await db.Contacts.CountAsync(Token));
            Assert.Equal(1, await db.Products.Where(p => p.Code == "ZN-CR-5").Select(p => p.Id).SingleAsync(Token));
            Assert.Equal(1, await db.BillAccounts.Where(b => b.Code == "BS1").Select(b => b.Id).SingleAsync(Token));

            // The oldest row (H0038, 90 days ago) is transaction 1; the newest (H0043, today) is 86.
            var first = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == 1, Token);
            Assert.Equal("seed-H0038", first.IdempotencyKey);
            Assert.Equal("ZC-0000000001", first.Reference);
            Assert.Equal(SeedData.At(SeedData.TodayIn(now), 90, "10:10"), first.CreatedAt);
            Assert.Equal("seed-H0043", await db.Transactions.Where(t => t.Id == 86).Select(t => t.IdempotencyKey).SingleAsync(Token));

            Assert.Equal(1, await db.Orders.MinAsync(o => o.Id, Token));
            Assert.Equal(9, await db.Orders.CountAsync(Token));
            Assert.Equal(9, await db.Aliases.CountAsync(Token));
        }
        finally
        {
            // Leave the shared database at the seed for the other tests in the collection.
            await fixture.ResetAsync();
        }
    }

    [Fact]
    public async Task HistoryIsDatedRelativeToToday()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await fixture.ResetAsync();
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var (seed, now) = SeedAndNow(scope);
        var today = SeedData.TodayIn(now);
        var rows = seed.History.ToDictionary(h => "seed-" + h.Id, StringComparer.Ordinal);

        var transactions = await db.Transactions.AsNoTracking().OrderBy(t => t.Id).ToListAsync(Token);

        Assert.Equal(rows.Count, transactions.Count);
        Assert.All(transactions, t =>
        {
            var row = rows[t.IdempotencyKey];
            Assert.Equal(SeedData.IraqOffset, t.CreatedAt.Offset);
            Assert.Equal(today.AddDays(-row.DaysAgo), DateOnly.FromDateTime(t.CreatedAt.DateTime));
            Assert.Equal(TimeOnly.ParseExact(row.Time, "HH:mm", CultureInfo.InvariantCulture), TimeOnly.FromTimeSpan(t.CreatedAt.TimeOfDay));
            Assert.Equal(t.CreatedAt, t.CompletedAt);
            Assert.Equal(TransactionStatus.Done, t.Status);
            Assert.Equal(row.Fee, t.FeeIqd);
        });

        // Ids follow time: the history was inserted oldest first.
        Assert.Equal(transactions.OrderBy(t => t.CreatedAt).Select(t => t.Id), transactions.Select(t => t.Id));
    }

    [Fact]
    public async Task MigrateOnTheOldSeedWipesAndReseeds()
    {
        // Not a migration replay (the old schema is gone). The migration's wipe is proven by the fixture's
        // EnsureDeleted + Migrate + Apply path producing exactly the seed counts; here, a row the seed does not hold
        // survives Apply (users exist, so it does nothing) and is gone after a reset.
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var (seed, now) = SeedAndNow(scope);
        try
        {
            db.Users.Add(new User { Code = "OLD", Phone = "07700001001", DisplayName = "Old seed user", PinHash = "pbkdf2$old" });
            await db.SaveChangesAsync(Token);

            SeedData.Apply(db, seed, now);
            Assert.Equal(6, await db.Users.CountAsync(Token));

            await SeedReset.ResetAsync(db, seed, now, Token);
            db.ChangeTracker.Clear();

            Assert.False(await db.Users.AnyAsync(u => u.Code == "OLD", Token));
            Assert.Equal(seed.Users.Count, await db.Users.CountAsync(Token));
            Assert.Equal(seed.Accounts.Count, await db.Accounts.CountAsync(Token));
            Assert.Equal(seed.Contacts.Count, await db.Contacts.CountAsync(Token));
            Assert.Equal(seed.History.Count, await db.Transactions.CountAsync(Token));
        }
        finally
        {
            await fixture.ResetAsync();
        }
    }

    // The seed and the clock the API under test uses.
    private static (SeedSet Seed, DateTimeOffset Now) SeedAndNow(AsyncServiceScope scope)
        => (scope.ServiceProvider.GetRequiredService<SeedSet>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow());
}
