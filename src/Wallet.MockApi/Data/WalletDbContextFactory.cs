using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wallet.MockApi.Data;

/// <summary>
/// Builds the context for the EF Core tools (dotnet ef) without starting the web host.
/// Reads the connection string from the same sources as the running app, in the same order:
/// appsettings.json, appsettings.{environment}.json, user secrets, environment variables.
/// </summary>
public class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public const string ConnectionStringName = "Wallet";

    public const string MissingConnectionStringMessage =
        "No connection string 'Wallet'. Set ConnectionStrings:Wallet in appsettings, user secrets or the ConnectionStrings__Wallet environment variable.";

    public WalletDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets(typeof(WalletDbContextFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(MissingConnectionStringMessage);

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new WalletDbContext(options);
    }
}
