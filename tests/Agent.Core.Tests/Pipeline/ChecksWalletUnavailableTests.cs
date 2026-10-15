using System.Net;
using System.Text;
using Agent.Core.Pipeline;
using Agent.Core.Tests.Agent;
using Agent.Core.Wallet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agent.Core.Tests.Pipeline;

/// <summary>
/// The checks when the wallet gives no answer, over a stub handler (no wallet, no database): a history read or a
/// quote that is unavailable is <c>not_done_nothing_taken</c> with the reason, and the checks before them need no wallet.
/// </summary>
public class ChecksWalletUnavailableTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CheckInput Transfer(long amount, string? status = "active")
        => new(1, "transfer", amount, "07805550101", 10, status, null, null, SkipFormat: false);

    private static (Checks Checks, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var wallet = new WalletClient(http, NullLogger<WalletClient>.Instance);
        return (new Checks(wallet, new FixedSnapshot(new AgentOptions()), new FakeClock()), handler);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task HistoryUnavailableIsNotDone()
    {
        var (checks, _) = Create(_ => throw new HttpRequestException("down"));

        Assert.Equal(
            new CheckResult.Fail(ReasonCodes.NotDoneNothingTaken, "wallet_connection", null),
            await checks.RunAsync(Transfer(50_000), Token));
    }

    [Fact]
    public async Task QuoteUnavailableIsNotDone()
    {
        var (checks, _) = Create(request => request.RequestUri!.AbsolutePath.EndsWith("/history", StringComparison.Ordinal)
            ? Json("[]")
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.Equal(
            new CheckResult.Fail(ReasonCodes.NotDoneNothingTaken, "wallet_http_500", null),
            await checks.RunAsync(Transfer(50_000), Token));
    }

    [Fact]
    public async Task EarlyChecksCallNoWallet()
    {
        var (checks, handler) = Create(_ => throw new HttpRequestException("down"));

        Assert.Equal(new CheckResult.Fail(ReasonCodes.AmountBelowMinimum, null, 1_000), await checks.RunAsync(Transfer(999), Token));
        Assert.Equal(new CheckResult.Fail(ReasonCodes.AccountClosed, null, null), await checks.RunAsync(Transfer(50_000, "closed"), Token));
        Assert.Equal(new CheckResult.Fail(ReasonCodes.OverPerTransactionCap, null, 5_000_000), await checks.RunAsync(Transfer(5_000_001), Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DailyCapCountsOnlyDoneRowsOfEveryType()
    {
        // 4,000,000 transfer done, 2,000,000 bill done, 3,000,000 failed: 6,000,000 counts, so 4,000,000 more fits and 4,000,001 does not.
        const string history = """
            [
              {"transactionId":3,"reference":"r3","type":"transfer","status":"done","amountIqd":4000000,"feeIqd":1000,"totalIqd":4001000,"createdAt":"2026-10-14T09:00:00+03:00"},
              {"transactionId":2,"reference":"r2","type":"bill","status":"done","amountIqd":2000000,"feeIqd":500,"totalIqd":2000500,"createdAt":"2026-10-14T08:00:00+03:00"},
              {"transactionId":1,"reference":"r1","type":"transfer","status":"failed","amountIqd":3000000,"feeIqd":1000,"totalIqd":3001000,"createdAt":"2026-10-14T07:00:00+03:00"}
            ]
            """;
        var (checks, _) = Create(request => request.RequestUri!.AbsolutePath.EndsWith("/history", StringComparison.Ordinal)
            ? Json(history)
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.Equal(new CheckResult.Fail(ReasonCodes.OverDailyCap, null, 4_000_000), await checks.RunAsync(Transfer(4_000_001), Token));
        Assert.Equal(
            new CheckResult.Fail(ReasonCodes.NotDoneNothingTaken, "wallet_http_500", null),
            await checks.RunAsync(Transfer(4_000_000), Token));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>The options of every scope: fixed values, as <c>Options.Create</c> gives for <see cref="IOptions{TOptions}"/>.</summary>
    private sealed class FixedSnapshot(AgentOptions value) : IOptionsSnapshot<AgentOptions>
    {
        public AgentOptions Value => value;

        public AgentOptions Get(string? name) => value;
    }
}
