using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Seed;

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

    /// <summary>
    /// The value every client of this fixture sends in <c>X-Wallet-Client-Key</c>; with no client key configured, any
    /// non-empty value opens the wallet routes in Development.
    /// </summary>
    public const string DevelopmentClientHeaderValue = "tests";

    private const int ProbeTimeoutSeconds = 3;

    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;

    public WalletApiFixture()
        : this(databaseName: null)
    {
    }

    /// <summary>
    /// A fixture on its own test database, <paramref name="databaseName"/> on the same server, so that another
    /// collection (the agent's) can run in parallel with this one without dropping its database. Null keeps the
    /// database of <c>ConnectionStrings__WalletTest</c> or the default.
    /// </summary>
    internal WalletApiFixture(string? databaseName)
    {
        var connection = new SqlConnectionStringBuilder(ReadTestConnectionString());
        if (databaseName is not null)
            connection.InitialCatalog = databaseName;
        TestConnectionString = connection.ConnectionString;
    }

    /// <summary>The connection string of the test database: <c>ConnectionStrings__WalletTest</c> or the default, with the database name given.</summary>
    public string TestConnectionString { get; }

    public bool DatabaseAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    /// <summary>
    /// A client of the API under test, sending the <c>X-Wallet-Client-Key</c> and <c>X-Wallet-Admin-Key</c> headers on
    /// every request. Only valid when <see cref="DatabaseAvailable"/> is true.
    /// </summary>
    public HttpClient Client => client ?? throw new InvalidOperationException("The wallet API is not running. " + SkipReason);

    private WebApplicationFactory<Program> Factory
        => factory ?? throw new InvalidOperationException("The wallet API is not running. " + SkipReason);

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
            // No admin or client key (empty overrides any key in user secrets and means none): the Development rule applies.
            host.UseSetting("Wallet:AdminKey", string.Empty);
            host.UseSetting("Wallet:ClientKey", string.Empty);
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
            SeedData.Apply(
                db,
                scope.ServiceProvider.GetRequiredService<SeedSet>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow());
        }

        client = CreateClient();

        // In Development with no admin key, the /admin routes still need the header, with any value.
        client.DefaultRequestHeaders.Add(AdminKeyFilter.HeaderName, DevelopmentAdminHeaderValue);
        DatabaseAvailable = true;
    }

    /// <summary>Returns the test database to the seed and clears the fault switch, through <c>POST /admin/reset</c>.</summary>
    public async Task ResetAsync()
    {
        using var response = await Client.PostAsync("/admin/reset", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A new client of the API under test, for tests that need their own timeout. It sends the
    /// <c>X-Wallet-Client-Key</c> header and no admin header. The caller disposes it.
    /// </summary>
    public HttpClient CreateClient()
    {
        var created = Factory.CreateClient();
        created.DefaultRequestHeaders.Add(ClientKeyFilter.HeaderName, DevelopmentClientHeaderValue);
        return created;
    }

    /// <summary>
    /// A handler that sends requests straight to the API under test, for an agent-side client built on its own
    /// <see cref="HttpClient"/>. It adds no headers. The caller disposes the client built on it.
    /// </summary>
    public HttpMessageHandler CreateHandler() => Factory.Server.CreateHandler();

    /// <summary>A service scope of the API under test, for tests that work on the database directly.</summary>
    public AsyncServiceScope CreateScope() => Factory.Services.CreateAsyncScope();

    /// <summary>
    /// The balance of the account with <paramref name="phone"/>, read from the database. Accounts behind contacts
    /// belong to no user, so <c>/users/{id}/balance</c> cannot report them.
    /// </summary>
    public async Task<long> AccountBalanceAsync(string phone)
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Accounts.AsNoTracking()
            .Where(a => a.Phone == phone)
            .Select(a => a.BalanceIqd)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

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
