using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

/// <summary>
/// Shared by the <see cref="WalletApiCollection"/>. Probes the test SQL Server once; when it is reachable,
/// recreates the test database, applies the migrations and the seed, and hosts the mock wallet API against it.
/// When it is not reachable, tests skip with <see cref="SkipReason"/>, unless <c>ZAINCASH_TEST_SQL=1</c>
/// demands the database, in which case initialisation fails.
/// </summary>
public sealed class WalletApiFixture : IAsyncLifetime
{
    public const string DefaultConnectionString =
        "Server=localhost;Database=ZainCashWallet_Test;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>The value <see cref="Client"/> sends in <c>X-Wallet-Admin-Key</c>; any non-empty value opens <c>/admin</c> in Development.</summary>
    public const string DevelopmentAdminHeaderValue = "tests";

    private const int ProbeTimeoutSeconds = 3;

    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;

    /// <summary>The connection string of the test database: <c>ConnectionStrings__WalletTest</c> or the default.</summary>
    public string TestConnectionString { get; } = ReadTestConnectionString();

    public bool DatabaseAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    /// <summary>
    /// A client of the API under test, sending the <c>X-Wallet-Admin-Key</c> header on every request. Only valid when
    /// <see cref="DatabaseAvailable"/> is true.
    /// </summary>
    public HttpClient Client => client ?? throw new InvalidOperationException("The wallet API is not running. " + SkipReason);

    private static bool DatabaseRequired => Environment.GetEnvironmentVariable("ZAINCASH_TEST_SQL") == "1";

    public async ValueTask InitializeAsync()
    {
        var builder = new SqlConnectionStringBuilder(TestConnectionString);

        // The fixture drops this database, so refuse anything that is not clearly a test database.
        if (!builder.InitialCatalog.EndsWith("_Test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The test database name must end with _Test, but was '{builder.InitialCatalog}'.");
        }

        if (!await ProbeAsync(builder))
        {
            SkipReason =
                $"SQL Server not reachable at {builder.DataSource}; set ConnectionStrings__WalletTest or start SQL Server";
            if (DatabaseRequired)
                throw new InvalidOperationException(SkipReason + " (ZAINCASH_TEST_SQL=1 requires the database).");
            return;
        }

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:Wallet", TestConnectionString);
            // No admin key (empty overrides any key in user secrets and means none): the Development rule applies.
            host.UseSetting("Wallet:AdminKey", string.Empty);
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

            // Before dropping anything, make sure the API under test really points at the test database.
            var hostDatabase = new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
            if (!string.Equals(hostDatabase, builder.InitialCatalog, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The API under test uses database '{hostDatabase}', not the test database '{builder.InitialCatalog}'.");
            }

            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
            SeedData.Apply(db);
        }

        client = factory.CreateClient();

        // In Development with no admin key, the /admin routes still need the header, with any value.
        client.DefaultRequestHeaders.Add(AdminKeyFilter.HeaderName, DevelopmentAdminHeaderValue);
        DatabaseAvailable = true;
    }

    /// <summary>Returns the test database to the frozen seed and clears the fault switch, through <c>POST /admin/reset</c>.</summary>
    public async Task ResetAsync()
    {
        using var response = await Client.PostAsync("/admin/reset", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A new client of the API under test, for tests that need their own timeout. It sends no admin header.
    /// The caller disposes it.
    /// </summary>
    public HttpClient CreateClient()
        => (factory ?? throw new InvalidOperationException("The wallet API is not running. " + SkipReason)).CreateClient();

    /// <summary>A service scope of the API under test, for tests that work on the database directly.</summary>
    public AsyncServiceScope CreateScope()
        => (factory ?? throw new InvalidOperationException("The wallet API is not running. " + SkipReason))
            .Services.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null)
            await factory.DisposeAsync();
    }

    private static string ReadTestConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("ConnectionStrings__WalletTest");
        return string.IsNullOrWhiteSpace(value) ? DefaultConnectionString : value;
    }

    // Connects to master, so the probe works before the test database exists.
    private static async Task<bool> ProbeAsync(SqlConnectionStringBuilder builder)
    {
        var probe = new SqlConnectionStringBuilder(builder.ConnectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = ProbeTimeoutSeconds,
            Pooling = false,
        };

        try
        {
            await using var connection = new SqlConnection(probe.ConnectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
