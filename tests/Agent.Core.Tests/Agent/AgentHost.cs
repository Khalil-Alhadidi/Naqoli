using Agent.Core.Data;
using Agent.Core.Tests.Wallet;
using Agent.Core.Wallet;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agent.Core.Tests.Agent;

/// <summary>
/// Shared by the <see cref="AgentCollection"/>. Hosts its own mock wallet (a <see cref="WalletApiFixture"/>), builds the
/// agent's services with <c>AddAgentCore</c> over an in-memory configuration, sends the wallet client to the in-process
/// wallet, and recreates the agent's test database. Skips, as the wallet fixture does, when SQL Server is unreachable.
/// </summary>
public sealed class AgentHost : IAsyncLifetime
{
    public const string DefaultConnectionString =
        "Server=localhost;Database=ZainCashAgent_Test;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>
    /// The wallet client key the agent sends in tests: at least 16 characters, as <c>AgentOptions</c> requires of any key
    /// set. The wallet under test has no key configured, so it accepts any value. A test value, not a secret.
    /// </summary>
    public const string TestWalletClientKey = "agent-tests-client-key";

    /// <summary>
    /// The base address the agent's client uses; the handler sends every request to the in-process wallet. It is
    /// localhost because the wallet's host filtering (<c>AllowedHosts</c>) answers any other host with 400.
    /// </summary>
    public const string WalletBaseUrl = "http://localhost";

    private ServiceProvider? services;

    /// <summary>
    /// The wallet database of this collection. The <c>wallet-api</c> collection runs in parallel on its own database,
    /// so this one has another name on the same server.
    /// </summary>
    public const string WalletDatabaseName = "ZainCashWalletAgent_Test";

    /// <summary>The agent's own mock wallet, on <see cref="WalletDatabaseName"/>.</summary>
    public WalletApiFixture Wallet { get; } = new(WalletDatabaseName);

    public FakeClock Clock { get; } = new();

    /// <summary>The connection string of the agent's test database: <c>ConnectionStrings__AgentTest</c> or the default.</summary>
    public string TestConnectionString { get; } = ReadTestConnectionString();

    public bool DatabaseAvailable => Wallet.DatabaseAvailable;

    public string SkipReason => Wallet.SkipReason;

    public IServiceProvider Services => services ?? throw new InvalidOperationException("The agent host is not running. " + SkipReason);

    public async ValueTask InitializeAsync()
    {
        var builder = new SqlConnectionStringBuilder(TestConnectionString);

        // The host drops this database, so refuse anything that is not clearly a test database.
        if (!builder.InitialCatalog.EndsWith("_Test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The agent test database name must end with _Test, but was '{builder.InitialCatalog}'.");
        }

        // The wallet fixture probes SQL Server and decides whether the database tests run or skip.
        await Wallet.InitializeAsync();
        if (!Wallet.DatabaseAvailable)
            return;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:WalletBaseUrl"] = WalletBaseUrl,
                ["Agent:WalletClientKey"] = TestWalletClientKey,
                ["ConnectionStrings:Agent"] = TestConnectionString,
            })
            .Build();

        var collection = new ServiceCollection();
        collection.AddLogging();
        // Registered before AddAgentCore, so its TryAddSingleton(TimeProvider.System) keeps this clock.
        collection.AddSingleton<TimeProvider>(Clock);
        collection.AddAgentCore(configuration);
        collection.AddHttpClient<WalletClient>().ConfigurePrimaryHttpMessageHandler(() => Wallet.CreateHandler());
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

        // Before dropping anything, make sure the context really points at the test database.
        var contextDatabase = new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
        if (!string.Equals(contextDatabase, builder.InitialCatalog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The agent context uses database '{contextDatabase}', not the test database '{builder.InitialCatalog}'.");
        }

        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    /// <summary>Returns the wallet to the seed and empties both agent tables.</summary>
    public async Task ResetAsync()
    {
        await Wallet.ResetAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var token = TestContext.Current.CancellationToken;
        await db.AuditEntries.ExecuteDeleteAsync(token);
        await db.PendingTransactions.ExecuteDeleteAsync(token);
    }

    /// <summary>
    /// A wallet client over its own <see cref="HttpClient"/> with <paramref name="timeout"/>, sending to the in-process
    /// wallet with the same options as the registered one. The host disposes nothing it returns; the client lives for the test.
    /// </summary>
    public WalletClient CreateClient(TimeSpan timeout)
    {
        var options = Services.GetRequiredService<IOptions<AgentOptions>>().Value;
        var http = new HttpClient(Wallet.CreateHandler());
        WalletClient.Configure(http, options, environment: null);
        http.Timeout = timeout;
        return new WalletClient(http, NullLogger<WalletClient>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (services is not null)
            await services.DisposeAsync();
        await Wallet.DisposeAsync();
    }

    private static string ReadTestConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("ConnectionStrings__AgentTest");
        return string.IsNullOrWhiteSpace(value) ? DefaultConnectionString : value;
    }
}
