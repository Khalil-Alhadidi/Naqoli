using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

public class SeedApplyTests
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("ZAINCASH_TEST_SQL") == "1";

    [Fact]
    public void ApplyTwiceDoesNotDuplicateUsers()
    {
        Assert.SkipUnless(Enabled, "Set ZAINCASH_TEST_SQL=1 to run against the local SQL Server.");

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer("Server=localhost;Database=ZainCashWallet_Test;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new WalletDbContext(options);
        db.Database.EnsureDeleted();
        db.Database.Migrate();

        SeedData.Apply(db);
        SeedData.Apply(db);

        Assert.Equal(SeedData.Users().Count, db.Users.Count());
        db.Database.EnsureDeleted();
    }
}
