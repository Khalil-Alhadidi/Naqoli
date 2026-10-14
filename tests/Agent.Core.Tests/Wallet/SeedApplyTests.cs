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
        using var db = new WalletDbContext(options);
        db.Database.EnsureDeleted();
        try
        {
            db.Database.Migrate();

            SeedData.Apply(db);
            SeedData.Apply(db);

            Assert.Equal(SeedData.Users().Count, db.Users.Count());
        }
        finally
        {
            db.Database.EnsureDeleted();
        }
    }
}
