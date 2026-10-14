using System.Net;
using System.Net.Http.Json;
using Agent.Core.Tests.Wallet;
using Agent.Core.Wallet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Wallet.MockApi.Contracts;

namespace Agent.Core.Tests.Agent;

/// <summary>The agent's typed wallet client against the in-process mock wallet: reads, quotes and the execute outcomes.</summary>
[Collection(AgentCollection.Name)]
public class WalletClientTests(AgentHost host)
{
    private const int DemoUserId = SeedFacts.DemoUserId;

    /// <summary>S20: U1's contact whose account is closed.</summary>
    private const string ClosedPhone = "07805550120";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private WalletClient Client => host.Services.GetRequiredService<WalletClient>();

    private static WalletPaymentRequest Transfer(int userId, long amount, string toPhone)
        => new(userId, "transfer", amount, toPhone, null, null, null, null);

    [Fact]
    public async Task ReadsUserContactsBillsBalanceProducts()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        var user = Assert.IsType<WalletResult<WalletUser>.Ok>(await Client.GetUserAsync(DemoUserId, Token)).Value;
        Assert.Equal(SeedFacts.DemoPhone, user.Phone);
        Assert.Equal("U1", user.Code);

        var contacts = Assert.IsType<WalletResult<WalletContacts>.Ok>(await Client.GetContactsAsync(DemoUserId, Token)).Value;
        Assert.Equal(20, contacts.Contacts.Count);
        Assert.Equal(4, contacts.Aliases.Count);

        var bills = Assert.IsType<WalletResult<IReadOnlyList<WalletBill>>.Ok>(await Client.GetBillsAsync(DemoUserId, Token)).Value;
        Assert.Equal(4, bills.Count);

        var balance = Assert.IsType<WalletResult<WalletBalance>.Ok>(await Client.GetBalanceAsync(DemoUserId, Token)).Value;
        Assert.Equal(SeedFacts.DemoBalance, balance.BalanceIqd);

        var products = Assert.IsType<WalletResult<IReadOnlyList<WalletProduct>>.Ok>(
            await Client.GetProductsAsync(null, null, null, Token)).Value;
        Assert.Equal(15, products.Count);

        var account = Assert.IsType<WalletResult<WalletAccount>.Ok>(
            await Client.GetAccountByPhoneAsync(SeedFacts.HammoudiPhone, Token)).Value;
        Assert.Equal("active", account.Status);

        var unknown = Assert.IsType<WalletResult<WalletUser>.Refused>(await Client.GetUserAsync(9_999, Token));
        Assert.Equal(new WalletProblem(404, "unknown_user", null, null), unknown.Problem);
    }

    [Fact]
    public async Task QuoteReturnsFiguresAndSufficient()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        var quote = Assert.IsType<WalletResult<WalletQuote>.Ok>(
            await Client.QuoteAsync(Transfer(DemoUserId, 50_000, SeedFacts.Contact("S01").Number), Token)).Value;

        Assert.Equal(50_000, quote.AmountIqd);
        Assert.Equal(1_000, quote.FeeIqd);
        Assert.Equal(51_000, quote.TotalIqd);
        Assert.Equal(699_000, quote.BalanceAfterIqd);
        Assert.True(quote.Sufficient);
    }

    [Fact]
    public async Task QuoteRefusalIsAProblem()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        var refused = Assert.IsType<WalletResult<WalletQuote>.Refused>(
            await Client.QuoteAsync(Transfer(DemoUserId, 10_000, ClosedPhone), Token));

        Assert.Equal(422, refused.Problem.Status);
        Assert.Equal("account_closed", refused.Problem.Code);
    }

    [Fact]
    public async Task ExecuteOutcomesMapStatusCodes()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
        var send = Transfer(DemoUserId, 50_000, SeedFacts.HammoudiPhone);

        try
        {
            // 201: the payment ran now.
            var first = Assert.IsType<ExecuteOutcome.Done>(await Client.ExecuteAsync("agent-k1", send, Token));
            Assert.False(first.Replayed);
            Assert.Equal("done", first.Transaction.Status);
            Assert.Equal(699_000, first.Transaction.BalanceAfterIqd);

            // 200: the same key and body again is a replay of the stored outcome.
            var again = Assert.IsType<ExecuteOutcome.Done>(await Client.ExecuteAsync("agent-k1", send, Token));
            Assert.True(again.Replayed);
            Assert.Equal(first.Transaction.TransactionId, again.Transaction.TransactionId);
            Assert.Equal(first.Transaction.BalanceAfterIqd, again.Transaction.BalanceAfterIqd);

            // 422: U2 holds 18,500, and 18,000 plus the 1,000 fee is more.
            var tight = new WalletPaymentRequest(
                SeedFacts.TightUserId, "transfer", 18_000, null, SeedFacts.DemoAccountId, null, null, null);
            var refused = Assert.IsType<ExecuteOutcome.Refused>(await Client.ExecuteAsync("agent-k2", tight, Token));
            Assert.Equal(422, refused.Problem.Status);
            Assert.Equal("insufficient_funds", refused.Problem.Code);

            // 500 from the fault switch: unknown, never failed.
            await SetFaultAsync(new FaultRequest("error", null));
            var error = Assert.IsType<ExecuteOutcome.Unknown>(await Client.ExecuteAsync("agent-k3", send, Token));
            Assert.Equal("http_500", error.Reason);

            // The wallet holds the answer longer than the client waits: unknown, and nothing moved.
            await SetFaultAsync(new FaultRequest("timeoutBeforeExecute", 20_000));
            var impatient = host.CreateClient(TimeSpan.FromSeconds(1));
            var timeout = Assert.IsType<ExecuteOutcome.Unknown>(await impatient.ExecuteAsync("agent-k4", send, Token));
            Assert.Equal("timeout", timeout.Reason);

            await SetFaultAsync(new FaultRequest("none", null));
            var balance = Assert.IsType<WalletResult<WalletBalance>.Ok>(await Client.GetBalanceAsync(DemoUserId, Token)).Value;
            Assert.Equal(699_000, balance.BalanceIqd);
            // Hammoudi's seeded 100,000 plus the one transfer that ran.
            Assert.Equal(150_000, await host.Wallet.AccountBalanceAsync(SeedFacts.HammoudiPhone));
            var missing = Assert.IsType<WalletResult<WalletTransaction>.Refused>(
                await Client.FindByKeyAsync("agent-k4", DemoUserId, Token));
            Assert.Equal("unknown_key", missing.Problem.Code);
        }
        finally
        {
            await host.ResetAsync();
        }
    }

    [Fact]
    public async Task FindByKeyNeedsUser()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
        Assert.IsType<ExecuteOutcome.Done>(
            await Client.ExecuteAsync("agent-find", Transfer(DemoUserId, 10_000, SeedFacts.HammoudiPhone), Token));

        var found = Assert.IsType<WalletResult<WalletTransaction>.Ok>(await Client.FindByKeyAsync("agent-find", DemoUserId, Token));
        Assert.Equal("done", found.Value.Status);

        var other = Assert.IsType<WalletResult<WalletTransaction>.Refused>(
            await Client.FindByKeyAsync("agent-find", SeedFacts.TraderUserId, Token));
        Assert.Equal(404, other.Problem.Status);
        Assert.Equal("unknown_key", other.Problem.Code);
    }

    [Theory]
    [InlineData("a-configured-client-key-01", "Production", "a-configured-client-key-01")]
    [InlineData(null, "Development", "dev")]
    public async Task ClientSendsTheKeyHeader(string? configuredKey, string environment, string expected)
    {
        var stub = new HeaderStub();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:WalletBaseUrl"] = "http://wallet",
                ["Agent:WalletClientKey"] = configuredKey,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubEnvironment(environment));
        services.AddAgentCore(configuration);
        services.AddHttpClient<WalletClient>().ConfigurePrimaryHttpMessageHandler(() => stub);
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<WalletClient>().GetBalanceAsync(DemoUserId, Token);

        Assert.IsType<WalletResult<WalletBalance>.Ok>(result);
        var request = Assert.Single(stub.Requests);
        Assert.Equal(expected, Assert.Single(request.Headers.GetValues("X-Wallet-Client-Key")));
        Assert.Equal(new Uri("http://wallet/users/1/balance"), request.RequestUri);
    }

    private async Task SetFaultAsync(FaultRequest fault)
    {
        using var response = await host.Wallet.Client.PostAsJsonAsync("/admin/faults", fault, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Records each request and answers with a balance, without any network.</summary>
    private sealed class HeaderStub : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { accountId = 1, balanceIqd = 750_000, asOf = DateTimeOffset.UnixEpoch }),
            });
        }
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Agent.Core.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
