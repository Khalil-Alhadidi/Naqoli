using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

[Collection(WalletApiCollection.Name)]
public class SeedApplyTests(WalletApiFixture fixture)
{
    [Fact]
    public async Task ApplyTwiceDoesNotDuplicateUsers()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        SeedData.Apply(db);
        SeedData.Apply(db);

        Assert.Equal(SeedData.Users().Count, db.Users.Count());
    }

    [Fact]
    public async Task ResetRestoresTheSeedWithIdentitiesFromOne()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        try
        {
            db.Accounts.Where(a => a.UserId == SeedData.DemoUserId).ExecuteUpdate(s => s.SetProperty(a => a.BalanceIqd, 1L));
            db.Contacts.Where(c => c.OwnerUserId == SeedData.DemoUserId).ExecuteDelete();

            SeedReset.Reset(db);
            db.ChangeTracker.Clear();

            Assert.Equal(12, db.Users.Count());
            Assert.Equal(12, db.Contacts.Count());
            Assert.Equal(SeedData.Transactions().Count, db.Transactions.Count());
            Assert.Equal(1, db.Users.Min(u => u.Id));
            Assert.Equal("07700001001", db.Users.Single(u => u.Id == SeedData.DemoUserId).Phone);
            Assert.Equal(Enumerable.Range(1, 12), db.Accounts.OrderBy(a => a.Id).Select(a => a.UserId).ToList());
            Assert.Equal("ZC-0000000001", db.Transactions.Single(t => t.Id == 1).Reference);
            Assert.Equal(1, db.Orders.Min(o => o.Id));
            Assert.Equal(300000, db.Accounts.Single(a => a.UserId == SeedData.DemoUserId).BalanceIqd);
        }
        finally
        {
            // Leave the shared database at the seed for the other tests in the collection.
            await fixture.ResetAsync();
        }
    }
}
