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

            // 500 injected_error from the fault switch: the wallet refused before writing, so it is a refusal.
            await SetFaultAsync(new FaultRequest("error", null));
            var error = Assert.IsType<ExecuteOutcome.Refused>(await Client.ExecuteAsync("agent-k3", send, Token));
            Assert.Equal((500, "injected_error"), (error.Problem.Status, error.Problem.Code));

            // The wallet holds the answer longer than the client waits: unknown, and nothing moved.
            await SetFaultAsync(new FaultRequest("timeoutBeforeExecute", 3_000));
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

    [Fact]
    public async Task ReadsReturnUnavailableOnServerErrorAndTimeout()
    {
        var stub = new ScriptedStub();
        var client = StubClient(stub);

        stub.Next = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var error = Assert.IsType<WalletResult<WalletBalance>.Unavailable>(await client.GetBalanceAsync(DemoUserId, Token));
        Assert.Equal("http_500", error.Reason);

        stub.Next = (_, _) => throw new HttpRequestException("refused");
        var connection = Assert.IsType<WalletResult<WalletQuote>.Unavailable>(
            await client.QuoteAsync(Transfer(DemoUserId, 10_000, SeedFacts.HammoudiPhone), Token));
        Assert.Equal("connection", connection.Reason);

        stub.Next = async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var timeout = Assert.IsType<WalletResult<bool>.Unavailable>(await client.GiveConsentAsync(DemoUserId, Token));
        Assert.Equal("timeout", timeout.Reason);

        stub.Next = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json") });
        var unreadable = Assert.IsType<WalletResult<WalletTransaction>.Unavailable>(
            await client.FindByKeyAsync("k", DemoUserId, Token));
        Assert.Equal("unreadable_response", unreadable.Reason);

        // The caller's own cancellation is not an outcome: it propagates.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserAsync(DemoUserId, cancelled.Token));
    }

    [Fact]
    public async Task ExecuteConflictIsUnknownInProgress()
    {
        var stub = new ScriptedStub
        {
            Next = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new { code = "in_progress" }, mediaType: new("application/problem+json")),
            }),
        };

        var outcome = await StubClient(stub).ExecuteAsync("k-409", Transfer(DemoUserId, 10_000, SeedFacts.HammoudiPhone), Token);

        Assert.Equal(new ExecuteOutcome.Unknown("in_progress"), outcome);
    }

    [Fact]
    public async Task ExecuteServerErrorWithAWalletCodeIsRefused()
    {
        var stub = new ScriptedStub();
        var client = StubClient(stub);
        var send = Transfer(DemoUserId, 10_000, SeedFacts.HammoudiPhone);

        stub.Next = (_, _) => Task.FromResult(Problem(HttpStatusCode.InternalServerError, """{"status":500,"title":"Injected error","code":"injected_error"}"""));
        var error = Assert.IsType<ExecuteOutcome.Refused>(await client.ExecuteAsync("k-500", send, Token));
        Assert.Equal(new WalletProblem(500, "injected_error", null, null), error.Problem);

        stub.Next = (_, _) => Task.FromResult(Problem(HttpStatusCode.GatewayTimeout, """{"status":504,"title":"Injected timeout","code":"injected_timeout"}"""));
        var timeout = Assert.IsType<ExecuteOutcome.Refused>(await client.ExecuteAsync("k-504", send, Token));
        Assert.Equal(new WalletProblem(504, "injected_timeout", null, null), timeout.Problem);
    }

    [Fact]
    public async Task ExecuteBareServerErrorIsUnknown()
    {
        var stub = new ScriptedStub();
        var client = StubClient(stub);
        var send = Transfer(DemoUserId, 10_000, SeedFacts.HammoudiPhone);

        // No body at all.
        stub.Next = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Equal(new ExecuteOutcome.Unknown("http_500"), await client.ExecuteAsync("k-1", send, Token));

        // A 5xx with a code that is not one of the two documented fault codes: nothing says nothing was written (review M1).
        stub.Next = (_, _) => Task.FromResult(Problem(HttpStatusCode.InternalServerError, """{"status":500,"title":"Other","code":"database_busy"}"""));
        Assert.Equal(new ExecuteOutcome.Unknown("http_500"), await client.ExecuteAsync("k-0", send, Token));

        // ProblemDetails from ASP.NET Core itself: no code.
        stub.Next = (_, _) => Task.FromResult(Problem(HttpStatusCode.InternalServerError, """{"status":500,"title":"An error occurred"}"""));
        Assert.Equal(new ExecuteOutcome.Unknown("http_500"), await client.ExecuteAsync("k-2", send, Token));

        // A body that cannot be read.
        stub.Next = (_, _) => Task.FromResult(Problem(HttpStatusCode.BadGateway, "{not json"));
        Assert.Equal(new ExecuteOutcome.Unknown("http_502"), await client.ExecuteAsync("k-3", send, Token));

        // Not JSON at all (a proxy's page).
        stub.Next = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("<html>code</html>", System.Text.Encoding.UTF8, "text/html"),
        });
        Assert.Equal(new ExecuteOutcome.Unknown("http_503"), await client.ExecuteAsync("k-4", send, Token));

        // A transport failure and the client's timeout stay unknown.
        stub.Next = (_, _) => throw new HttpRequestException("refused");
        Assert.Equal(new ExecuteOutcome.Unknown("connection"), await client.ExecuteAsync("k-5", send, Token));

        stub.Next = async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        };
        Assert.Equal(new ExecuteOutcome.Unknown("timeout"), await client.ExecuteAsync("k-6", send, Token));
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/problem+json") };

    private static WalletClient StubClient(HttpMessageHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("http://wallet"), Timeout = TimeSpan.FromSeconds(1) },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WalletClient>.Instance);

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

    /// <summary>Answers each request with whatever <see cref="Next"/> does, without any network.</summary>
    private sealed class ScriptedStub : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Next { get; set; } =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Next(request, cancellationToken);
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Agent.Core.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
