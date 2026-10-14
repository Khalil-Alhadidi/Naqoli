using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

public class SeedApplyTests
{
    private const string DefaultConnectionString =
        "Server=localhost;Database=ZainCashWallet_Test;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly bool Enabled = Environment.GetEnvironmentVariable("ZAINCASH_TEST_SQL") == "1";

    [Fact]
    public void ApplyTwiceDoesNotDuplicateUsers()
    {
        using var db = CreateFreshDatabase();
        try
        {
            SeedData.Apply(db);
            SeedData.Apply(db);

            Assert.Equal(SeedData.Users().Count, db.Users.Count());
        }
        finally
        {
            db.Database.EnsureDeleted();
        }
    }

    [Fact]
    public void ResetRestoresTheSeedWithIdentitiesFromOne()
    {
        using var db = CreateFreshDatabase();
        try
        {
            SeedData.Apply(db);
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
            db.Database.EnsureDeleted();
        }
    }

    private static WalletDbContext CreateFreshDatabase()
    {
        Assert.SkipUnless(Enabled, "Set ZAINCASH_TEST_SQL=1 to run against the local SQL Server.");

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__WalletTest");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = DefaultConnectionString;

        // This test drops its database, so refuse anything that is not clearly a test database.
        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        Assert.True(
            databaseName.EndsWith("_Test", StringComparison.OrdinalIgnoreCase),
            $"The test database name must end with _Test, but was '{databaseName}'.");

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var db = new WalletDbContext(options);
        db.Database.EnsureDeleted();
        db.Database.Migrate();
        return db;
    }
}
