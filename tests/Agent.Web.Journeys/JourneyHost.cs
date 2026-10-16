extern alias AgentWeb;

using System.Net;
using System.Net.Http.Json;
using Agent.Core.Data;
using Agent.Core.Pipeline;
using Agent.Core.Wallet;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Wallet.MockApi;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Seed;
using AgentWeb::Agent.Web.Services;
using WalletProgram = Program;
using WebProgram = AgentWeb::Program;

namespace Agent.Web.Journeys;

/// <summary>
/// Hosts the mock wallet and the web console in process on Kestrel (dynamic ports), each on its own test database,
/// and one headless Chromium for the journeys. Two console hosts share the agent database: one with the default card
/// timer and one with <see cref="ShortCardTimerSeconds"/> for the timer journey (both for U1), and one console each for
/// <see cref="OtherUserIds"/> (<see cref="UserWebAddress"/>). The journeys use the scripted parser, never a live model.
/// Skips when SQL Server or Chromium is missing.
/// </summary>
public sealed class JourneyHost : IAsyncLifetime
{
    public const string DefaultWalletConnectionString =
        "Server=localhost;Database=ZainCashWalletJourneys_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public const string DefaultAgentConnectionString =
        "Server=localhost;Database=ZainCashAgent_Journeys_Test;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>The wallet client key the console sends: a test value, not a secret.</summary>
    public const string ClientKey = "journeys-client-key-0001";

    /// <summary>Any non-empty value opens the wallet's admin routes in Development when no admin key is configured.</summary>
    public const string AdminKey = "journeys";

    /// <summary>The card timer of the timer journey's console.</summary>
    public const int ShortCardTimerSeconds = 4;

    public const string InstallCommand = "pwsh tests/Agent.Web.Journeys/bin/Debug/net10.0/playwright.ps1 install chromium";

    /// <summary>The users that get a console of their own besides U1: U2 (tight balance), U3 (trader, PIN), U5 (no consent).</summary>
    public static readonly int[] OtherUserIds = [2, 3, 5];

    private const int ProbeTimeoutSeconds = 3;

    private readonly Dictionary<int, (WebApplicationFactory<WebProgram> Factory, Uri Address)> userWebs = [];

    private WebApplicationFactory<WalletProgram>? wallet;
    private WebApplicationFactory<WebProgram>? web;
    private WebApplicationFactory<WebProgram>? shortTimerWeb;
    private IPlaywright? playwright;

    public string WalletConnectionString { get; } = Read("ConnectionStrings__WalletJourneysTest", DefaultWalletConnectionString);

    public string AgentConnectionString { get; } = Read("ConnectionStrings__AgentJourneysTest", DefaultAgentConnectionString);

    /// <summary>Why the journeys skip; null when they can run.</summary>
    public string? SkipReason { get; private set; }

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>A client of the wallet that sends both keys.</summary>
    public HttpClient Wallet { get; private set; } = null!;

    /// <summary>The console with the default card timer.</summary>
    public Uri WebAddress { get; private set; } = null!;

    /// <summary>The console with <see cref="ShortCardTimerSeconds"/>.</summary>
    public Uri ShortTimerWebAddress { get; private set; } = null!;

    public IServiceProvider WebServices => web?.Services ?? throw new InvalidOperationException("The console is not running.");

    /// <summary>The console whose demo user is <paramref name="userId"/> (2, 3 or 5), default card timer.</summary>
    public Uri UserWebAddress(int userId)
        => userWebs.TryGetValue(userId, out var console)
            ? console.Address
            : throw new ArgumentOutOfRangeException(nameof(userId), userId, "No console for this user.");

    /// <summary>Every console's services: U1's two, then the other users'.</summary>
    private IEnumerable<IServiceProvider> AllWebServices
        => new[] { web, shortTimerWeb }.Concat(userWebs.Values.Select(u => u.Factory)).OfType<WebApplicationFactory<WebProgram>>().Select(f => f.Services);

    public async ValueTask InitializeAsync()
    {
        foreach (var connection in new[] { WalletConnectionString, AgentConnectionString })
        {
            // The host drops these databases, so refuse anything that is not clearly a test database.
            var name = new SqlConnectionStringBuilder(connection).InitialCatalog;
            if (!name.EndsWith("_Test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"A journey database name must end with _Test, but was '{name}'.");
        }

        var server = new SqlConnectionStringBuilder(WalletConnectionString);
        if (!await ProbeAsync(server))
        {
            SkipReason = $"SQL Server not reachable at {server.DataSource}; set ConnectionStrings__WalletJourneysTest and ConnectionStrings__AgentJourneysTest or start SQL Server";
            return;
        }

        try
        {
            playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException ex)
        {
            SkipReason = "Chromium for Playwright is not installed (" + ex.Message.Split('\n')[0] + "). Run: " + InstallCommand;
            return;
        }

        await StartWalletAsync();

        // The agent database first, so the consoles' sweepers find their tables from the first tick.
        var agentOptions = new DbContextOptionsBuilder<AgentDbContext>().UseSqlServer(AgentConnectionString).Options;
        await using (var db = new AgentDbContext(agentOptions))
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        web = StartWeb(cardTimerSeconds: null, demoUserId: null, out var webAddress);
        WebAddress = webAddress;
        shortTimerWeb = StartWeb(ShortCardTimerSeconds, demoUserId: null, out var shortAddress);
        ShortTimerWebAddress = shortAddress;
        foreach (var userId in OtherUserIds)
        {
            var factory = StartWeb(cardTimerSeconds: null, userId, out var address);
            userWebs[userId] = (factory, address);
        }
    }

    /// <summary>Returns the wallet to the seed and empties the agent's tables, before each journey.</summary>
    public async Task ResetAsync()
    {
        using (var response = await Wallet.PostAsync("/admin/reset", null))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = WebServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        await db.AuditEntries.ExecuteDeleteAsync();
        await db.PendingTransactions.ExecuteDeleteAsync();
        await db.LearningEntries.ExecuteDeleteAsync();

        // A journey that stopped half way must not leave the kill switch on or a saved setting behind for the next one.
        // The wallet's reset above clears its fault switch; each console's model fault is cleared here (milestone 8).
        foreach (var services in AllWebServices)
        {
            services.GetRequiredService<KillSwitch>().Set(false);
            services.GetRequiredService<ModelFaultSwitch>().Clear();
            var runtime = services.GetRequiredService<RuntimeSettings>();
            foreach (var key in runtime.Overrides.Keys.ToList())
                runtime.Reset(key);
        }
    }

    /// <summary>The wallet's account behind <paramref name="phone"/>.</summary>
    public async Task<WalletAccount> AccountByPhoneAsync(string phone)
        => (await Wallet.GetFromJsonAsync<WalletAccount>("/accounts/by-phone/" + Uri.EscapeDataString(phone)))!;

    /// <summary>The wallet user <paramref name="userId"/>, with whether consent is given.</summary>
    public async Task<WalletUser> UserAsync(int userId)
        => (await Wallet.GetFromJsonAsync<WalletUser>($"/users/{userId}"))!;

    /// <summary>The demo user's balance, from the wallet.</summary>
    public async Task<long> BalanceAsync(int userId)
        => (await Wallet.GetFromJsonAsync<WalletBalance>($"/users/{userId}/balance"))!.BalanceIqd;

    /// <summary>The demo user's history, newest first.</summary>
    public async Task<IReadOnlyList<WalletHistoryItem>> HistoryAsync(int userId)
        => (await Wallet.GetFromJsonAsync<List<WalletHistoryItem>>($"/users/{userId}/history?take=200"))!;

    /// <summary>The user's bills, with the amount due now, from the wallet.</summary>
    public async Task<IReadOnlyList<WalletBill>> BillsAsync(int userId)
        => (await Wallet.GetFromJsonAsync<List<WalletBill>>($"/users/{userId}/bills"))!;

    /// <summary>The wallet's fault switch now, as <c>GET /admin/faults</c> reports it (mode name only).</summary>
    public async Task<string> WalletFaultModeAsync()
    {
        using var document = System.Text.Json.JsonDocument.Parse(await Wallet.GetStringAsync("/admin/faults"));
        return document.RootElement.GetProperty("mode").GetString()!;
    }

    public async Task<WalletContacts> ContactsAsync(int userId)
        => (await Wallet.GetFromJsonAsync<WalletContacts>($"/users/{userId}/contacts"))!;

    public async ValueTask DisposeAsync()
    {
        Wallet?.Dispose();
        if (Browser is not null)
            await Browser.DisposeAsync();
        playwright?.Dispose();
        foreach (var (factory, _) in userWebs.Values)
            await factory.DisposeAsync();
        if (shortTimerWeb is not null)
            await shortTimerWeb.DisposeAsync();
        if (web is not null)
            await web.DisposeAsync();
        if (wallet is not null)
            await wallet.DisposeAsync();
    }

    /// <summary>Hosts the wallet on its own test database, recreated, migrated and seeded as the wallet tests do.</summary>
    private async Task StartWalletAsync()
    {
        wallet = new WebApplicationFactory<WalletProgram>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:Wallet", WalletConnectionString);
            // No keys configured (empty overrides user secrets): the Development rule, any non-empty header, applies.
            host.UseSetting("Wallet:AdminKey", string.Empty);
            host.UseSetting("Wallet:ClientKey", string.Empty);
        });
        wallet.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        wallet.StartServer();

        await using (var scope = wallet.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
            var hostDatabase = new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
            var expected = new SqlConnectionStringBuilder(WalletConnectionString).InitialCatalog;
            if (!string.Equals(hostDatabase, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The wallet uses database '{hostDatabase}', not the test database '{expected}'.");

            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
            SeedData.Apply(
                db,
                scope.ServiceProvider.GetRequiredService<SeedSet>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow());
        }

        Wallet = new HttpClient { BaseAddress = AddressOf(wallet.Services) };
        Wallet.DefaultRequestHeaders.Add(ClientKeyFilter.HeaderName, ClientKey);
        Wallet.DefaultRequestHeaders.Add(AdminKeyFilter.HeaderName, AdminKey);
    }

    private WebApplicationFactory<WebProgram> StartWeb(int? cardTimerSeconds, int? demoUserId, out Uri address)
    {
        var walletAddress = Wallet.BaseAddress!.ToString();
        var factory = new WebApplicationFactory<WebProgram>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Agent:WalletBaseUrl", walletAddress);
            host.UseSetting("Agent:WalletClientKey", ClientKey);
            host.UseSetting("ConnectionStrings:Agent", AgentConnectionString);
            host.UseSetting("Agent:SweepIntervalSeconds", "1");
            // Journeys never call a live model: the scripted parser is pinned whatever the developer's secrets say.
            host.UseSetting("Agent:Parser", "scripted");
            if (cardTimerSeconds is { } seconds)
                host.UseSetting("Agent:CardTimerSeconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (demoUserId is { } user)
                host.UseSetting("Agent:DemoUserId", user.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        factory.StartServer();
        address = AddressOf(factory.Services);
        return factory;
    }

    private static Uri AddressOf(IServiceProvider services)
    {
        var addresses = services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
            ?? throw new InvalidOperationException("The server reports no address.");
        return new Uri(addresses.First());
    }

    private static string Read(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    // Connects to master, so the probe works before the test databases exist.
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

[CollectionDefinition(Name)]
public sealed class JourneyCollection : ICollectionFixture<JourneyHost>
{
    public const string Name = "journeys";
}
