using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Agent.Core.Audit;
using Agent.Core.Confirm;
using Agent.Core.Data;
using Agent.Core.Pending;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;
using Agent.Core.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Confirm;

/// <summary>
/// The Confirm tap against the agent's in-process wallet: money moves once and only from a live card (rule 1), an
/// unknown outcome is Checking and is settled under the same key (rule 6), and the PIN keeps or ends the card without
/// ever being stored.
/// </summary>
[Collection(AgentCollection.Name)]
public class ConfirmHandlerTests(AgentHost host)
{
    private const int U1 = SeedFacts.DemoUserId;
    private const int U2 = SeedFacts.TightUserId;
    private const int U3 = SeedFacts.TraderUserId;

    private const string TraderPhone = "07905550003";
    private const string TightPhone = "07705550002";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ParsedRequest FiftyThousandToHammoudi
        => ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50_000, AmountUnit.Dinars);

    /// <summary>U3 to T01, above the PIN threshold.</summary>
    private static ParsedRequest TraderTransferWithPin
        => ParsedRequest.Transfer(SeedFacts.Contact("T01").SavedName, 1_100_000, AmountUnit.Dinars);

    private static string TraderPin => SeedFacts.PinOf("U3");

    /// <summary>A well-formed PIN that is not U3's, and not inside any figure, phone or reference these tests store.</summary>
    private static string WrongPin => TraderPin == "7531" ? "7532" : "7531";

    private async Task StartAsync()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
    }

    private async Task<PendingRecord> CardAsync(int userId, ParsedRequest request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reply = await scope.ServiceProvider.GetRequiredService<Orchestrator>().HandleAsync(userId, request, Token);
        return Assert.IsType<AgentReply.CardIssued>(reply).Pending;
    }

    private async Task<ConfirmOutcome> ConfirmAsync(int userId, Guid id, string? pin = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConfirmHandler>().ConfirmAsync(userId, id, pin, Token);
    }

    private async Task<ConfirmOutcome> SettleAsync(int userId, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConfirmHandler>().SettleCheckingAsync(userId, id, Token);
    }

    /// <summary>A Confirm through a handler whose wallet client gives up after one second.</summary>
    private Task<ConfirmOutcome> ConfirmImpatientlyAsync(PendingRecord card)
        => ConfirmThroughAsync(host.CreateClient(TimeSpan.FromSeconds(1)), card);

    /// <summary>A Confirm through a handler (and its checks) that reach the wallet through <paramref name="client"/>.</summary>
    private async Task<ConfirmOutcome> ConfirmThroughAsync(WalletClient client, PendingRecord card, string? pin = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var checks = ActivatorUtilities.CreateInstance<Checks>(scope.ServiceProvider, client);
        var handler = ActivatorUtilities.CreateInstance<ConfirmHandler>(scope.ServiceProvider, client, checks);
        return await handler.ConfirmAsync(card.UserId, card.Id, pin, Token);
    }

    /// <summary>A wallet client whose <c>POST /transactions</c> is answered by <paramref name="execute"/>; every other call reaches the wallet.</summary>
    private WalletClient StubbedExecuteClient(Func<HttpResponseMessage> execute)
        => host.CreateClient(new StubExecuteHandler(execute) { InnerHandler = host.Wallet.CreateHandler() }, TimeSpan.FromSeconds(10));

    /// <summary>
    /// A Confirm whose execute call gets a bare 500 (no body, no wallet code), as a lost database connection gives: the
    /// outcome is unknown, so the record goes to Checking. The stub writes nothing, so a resend executes normally.
    /// </summary>
    private Task<ConfirmOutcome> ConfirmIntoCheckingAsync(PendingRecord card, string? pin = null)
        => ConfirmThroughAsync(StubbedExecuteClient(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)), card, pin);

    /// <summary>The record as stored, read by its ID alone (a test read, not a user's).</summary>
    private async Task<PendingRecord> RecordAsync(Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var record = await db.PendingTransactions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, Token);
        Assert.NotNull(record);
        return record;
    }

    private async Task<List<AuditEntry>> AuditAsync(Guid? id = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.AuditEntries.AsNoTracking()
            .Where(a => id == null || a.PendingId == id)
            .OrderBy(a => a.Id)
            .ToListAsync(Token);
    }

    /// <summary>How many wallet transaction rows carry <paramref name="key"/>.</summary>
    private async Task<int> WalletRowsAsync(string key)
    {
        await using var scope = host.Wallet.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Transactions.AsNoTracking().CountAsync(t => t.IdempotencyKey == key, Token);
    }

    private async Task SetFaultAsync(FaultRequest request)
    {
        using var response = await host.Wallet.Client.PostAsJsonAsync("/admin/faults", request, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<WalletResult<WalletTransaction>> ByKeyAsync(string key, int userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WalletClient>().FindByKeyAsync(key, userId, Token);
    }

    /// <summary>Polls by-key until the payment under <paramref name="key"/> is done, for at most ten seconds.</summary>
    private async Task<WalletTransaction> WaitUntilDoneAsync(string key, int userId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            if (await ByKeyAsync(key, userId) is WalletResult<WalletTransaction>.Ok { Value: { Status: "done" } done })
                return done;
            Assert.True(DateTime.UtcNow < deadline, "The original payment did not finish within ten seconds.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }
    }

    private int SettleWindowSeconds => host.Services.GetRequiredService<IOptions<AgentOptions>>().Value.SettleWindowSeconds;

    /// <summary>
    /// Sends every request on without the client's cancellation, as a network does: when the client gives up, the
    /// in-process wallet does not hear of it and finishes the call. (The test server otherwise aborts the request.)
    /// </summary>
    private sealed class DetachedHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                await request.Content.LoadIntoBufferAsync(cancellationToken);
            return await base.SendAsync(request, CancellationToken.None).WaitAsync(cancellationToken);
        }
    }

    /// <summary>Answers <c>POST /transactions</c> itself and passes every other request on.</summary>
    private sealed class StubExecuteHandler(Func<HttpResponseMessage> execute) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/transactions"
                ? Task.FromResult(execute())
                : base.SendAsync(request, cancellationToken);
    }

    [Fact]
    public async Task ConfirmMovesMoneyOnce()
    {
        await StartAsync();
        var hammoudiBefore = await host.Wallet.AccountBalanceAsync(SeedFacts.HammoudiPhone);
        var card = await CardAsync(U1, FiftyThousandToHammoudi);

        var outcome = await ConfirmAsync(card.UserId, card.Id);

        Assert.Equal(new ConfirmOutcome.Done("ZC-0000000087", 699_000, null), outcome);
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(hammoudiBefore + 50_000, await host.Wallet.AccountBalanceAsync(SeedFacts.HammoudiPhone));
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));

        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.Done, record.State);
        Assert.Equal("ZC-0000000087", record.Reference);
        Assert.Equal(699_000, record.BalanceAfterIqd);
        Assert.Equal(1, record.Attempts);

        var rows = await AuditAsync(card.Id);
        Assert.Equal([AuditEvents.Created, AuditEvents.Executing, AuditEvents.Done], rows.Select(r => r.Event));
        Assert.Equal(50_000, rows[2].AmountIqd);
        Assert.Equal("ZC-0000000087", rows[2].Reference);

        // A second tap on the finished card does nothing.
        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.Done), await ConfirmAsync(card.UserId, card.Id));
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task DoubleTapExecutesOnce()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);

        var outcomes = await Task.WhenAll(ConfirmAsync(card.UserId, card.Id), ConfirmAsync(card.UserId, card.Id));

        Assert.Single(outcomes.OfType<ConfirmOutcome.Done>());
        Assert.Single(outcomes.OfType<ConfirmOutcome.AlreadyHandled>());
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Single(await AuditAsync(card.Id), r => r.Event == AuditEvents.Executing);
    }

    [Fact]
    public async Task ExpiredCannotExecute()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);

        host.Clock.Advance(TimeSpan.FromSeconds(46));
        var outcome = await ConfirmAsync(card.UserId, card.Id);

        Assert.IsType<ConfirmOutcome.Expired>(outcome);
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(PendingState.Expired, (await RecordAsync(card.Id)).State);
    }

    [Fact]
    public async Task CancelledCannotExecute()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        await using (var scope = host.Services.CreateAsyncScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<PendingStore>().CancelAsync(card.Id, card.UserId, Token));

        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.Cancelled), await ConfirmAsync(card.UserId, card.Id));
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task UnknownIdIsNotFound()
    {
        await StartAsync();
        Assert.IsType<ConfirmOutcome.NotFound>(await ConfirmAsync(U1, Guid.NewGuid()));
    }

    [Fact]
    public async Task BalanceChangedAfterTheCard()
    {
        await StartAsync();
        var card = await CardAsync(U2, ParsedRequest.Transfer(SeedFacts.Contact("Z01").Alias, 9_000, AmountUnit.Dinars));
        Assert.Equal(10_000, card.TotalIqd);

        // Money leaves U2's wallet from outside while the card waits: 9,500 plus the 1,000 fee, from 18,500 to 8,000.
        using (var message = new HttpRequestMessage(HttpMethod.Post, "/transactions")
        {
            Content = JsonContent.Create(new PaymentRequest(U2, "transfer", 9_500, null, SeedFacts.DemoAccountId, null, null, null)),
        })
        {
            message.Headers.Add(WalletClient.IdempotencyKeyHeader, "test-outside-debit");
            using var response = await host.Wallet.Client.SendAsync(message, Token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        Assert.Equal(8_000, await host.Wallet.AccountBalanceAsync(TightPhone));

        var outcome = await ConfirmAsync(card.UserId, card.Id);

        // The shortfall is the card's total less the balance now: 10,000 - 8,000.
        Assert.Equal(new ConfirmOutcome.NotDone(ReasonCodes.InsufficientBalance, null, 2_000), outcome);
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(8_000, await host.Wallet.AccountBalanceAsync(TightPhone));
        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.NotDone, record.State);
        Assert.Equal(ReasonCodes.InsufficientBalance, record.ResultCode);
    }

    [Fact]
    public async Task WalletErrorWithACodeIsNotDoneNothingTaken()
    {
        // FLT-03: the fault switch answers 500 injected_error before writing anything.
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        await SetFaultAsync(new FaultRequest("error", null, Once: true));

        var outcome = await ConfirmAsync(card.UserId, card.Id);

        Assert.Equal(new ConfirmOutcome.NotDone(ReasonCodes.NotDoneNothingTaken, "injected_error"), outcome);
        var record = await RecordAsync(card.Id);
        Assert.Equal((PendingState.NotDone, "injected_error"), (record.State, record.ResultCode));
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));

        // The record left Pending once: another tap does nothing, and the settle path does not apply.
        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.NotDone), await ConfirmAsync(card.UserId, card.Id));
        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.NotDone), await SettleAsync(card.UserId, card.Id));
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Single(await AuditAsync(card.Id), r => r.Event == AuditEvents.NotDone);
    }

    [Fact]
    public async Task StubbedServerErrorWithACodeIsNotDone()
    {
        await StartAsync();
        var coded = await CardAsync(U1, FiftyThousandToHammoudi);
        var client = StubbedExecuteClient(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                """{"status":500,"title":"Injected error","code":"injected_error"}""", Encoding.UTF8, "application/problem+json"),
        });

        Assert.Equal(
            new ConfirmOutcome.NotDone(ReasonCodes.NotDoneNothingTaken, "injected_error"),
            await ConfirmThroughAsync(client, coded));
        var record = await RecordAsync(coded.Id);
        Assert.Equal((PendingState.NotDone, "injected_error"), (record.State, record.ResultCode));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task BareWalletErrorIsCheckingThenDoneBySettle()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);

        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmIntoCheckingAsync(card));

        var checking = await RecordAsync(card.Id);
        Assert.Equal(PendingState.Checking, checking.State);
        Assert.Equal(card.IdempotencyKey, checking.IdempotencyKey);
        Assert.Equal("http_500", (await AuditAsync(card.Id)).Last().Reason);
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));

        // The bare 500 wrote nothing; the resend under the same key executes now.
        var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.UserId, card.Id));

        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
        var byKey = Assert.IsType<WalletResult<WalletTransaction>.Ok>(await ByKeyAsync(card.IdempotencyKey, U1)).Value;
        Assert.Equal(byKey.Reference, settled.Reference);
        Assert.Equal(699_000, settled.BalanceAfterIqd);
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(PendingState.Done, (await RecordAsync(card.Id)).State);
        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.Done), await SettleAsync(card.UserId, card.Id));
    }

    [Fact]
    public async Task TimeoutAfterExecuteIsCheckingThenDone()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        await SetFaultAsync(new FaultRequest("timeoutAfterExecute", 3_000, Once: true));

        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmImpatientlyAsync(card));
        Assert.Equal(PendingState.Checking, (await RecordAsync(card.Id)).State);
        Assert.Equal(WalletClient.Timeout, (await AuditAsync(card.Id)).Last().Reason);

        // The wallet paid before it went quiet: the key is already done, so the resend is a replay.
        var stored = Assert.IsType<WalletResult<WalletTransaction>.Ok>(await ByKeyAsync(card.IdempotencyKey, U1)).Value;
        Assert.Equal("done", stored.Status);

        var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.UserId, card.Id));

        Assert.Equal(stored.Reference, settled.Reference);
        Assert.Equal(699_000, settled.BalanceAfterIqd);
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(PendingState.Done, (await RecordAsync(card.Id)).State);

        await SetFaultAsync(new FaultRequest("none", null));
    }

    [Fact]
    public async Task WalletErrorOnASettleStaysChecking()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        await SetFaultAsync(new FaultRequest("timeoutAfterExecute", 3_000, Once: true));
        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmImpatientlyAsync(card));
        var stored = await WaitUntilDoneAsync(card.IdempotencyKey, U1);

        // The original call was written, but the fault switch answers before the key lookup: not a refusal of the card.
        await SetFaultAsync(new FaultRequest("error", null, Once: true));
        Assert.IsType<ConfirmOutcome.Checking>(await SettleAsync(card.UserId, card.Id));
        Assert.Equal(PendingState.Checking, (await RecordAsync(card.Id)).State);

        var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.UserId, card.Id));
        Assert.Equal(stored.Reference, settled.Reference);
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
    }

    [Fact]
    public async Task TimeoutBeforeExecuteIsCheckingThenDoneByResend()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        await SetFaultAsync(new FaultRequest("timeoutBeforeExecute", 2_000, Once: true));

        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmImpatientlyAsync(card));
        Assert.Equal(PendingState.Checking, (await RecordAsync(card.Id)).State);
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));

        // Nothing was written; the fault has cleared, so the resend executes now, once.
        var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.UserId, card.Id));

        Assert.Equal(699_000, settled.BalanceAfterIqd);
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));

        await SetFaultAsync(new FaultRequest("none", null));
    }

    [Fact]
    public async Task RetryReusesTheSameKey()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmIntoCheckingAsync(card));

        var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.UserId, card.Id));

        // The first call wrote nothing, so the only row under the record's key is the resend's.
        var byKey = Assert.IsType<WalletResult<WalletTransaction>.Ok>(await ByKeyAsync(card.IdempotencyKey, U1)).Value;
        Assert.Equal(settled.Reference, byKey.Reference);
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(card.IdempotencyKey, (await RecordAsync(card.Id)).IdempotencyKey);
    }

    [Fact]
    public async Task WrongPinKeepsTheCard()
    {
        await StartAsync();
        var traderBefore = await host.Wallet.AccountBalanceAsync(TraderPhone);
        var card = await CardAsync(U3, TraderTransferWithPin);
        Assert.True(card.PinRequired);

        Assert.IsType<ConfirmOutcome.PinRequired>(await ConfirmAsync(card.UserId, card.Id));
        Assert.Equal(PendingState.Pending, (await RecordAsync(card.Id)).State);

        Assert.Equal(new ConfirmOutcome.PinWrong(2), await ConfirmAsync(card.UserId, card.Id, WrongPin));
        Assert.Equal(PendingState.Pending, (await RecordAsync(card.Id)).State);

        // A malformed PIN costs no attempt: the next wrong one still leaves one.
        Assert.IsType<ConfirmOutcome.PinMalformed>(await ConfirmAsync(card.UserId, card.Id, "12"));
        Assert.Equal(PendingState.Pending, (await RecordAsync(card.Id)).State);
        Assert.Equal(new ConfirmOutcome.PinWrong(1), await ConfirmAsync(card.UserId, card.Id, WrongPin));

        // The right PIN, typed in Arabic-Indic digits, goes through.
        var done = Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(card.UserId, card.Id, ArabicDigits.ToArabicIndic(TraderPin)));

        Assert.Equal(traderBefore - 1_101_000, done.BalanceAfterIqd);
        Assert.Equal(traderBefore - 1_101_000, await host.Wallet.AccountBalanceAsync(TraderPhone));
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(
            [
                AuditEvents.Created,
                AuditEvents.Executing, AuditEvents.BackToPending,
                AuditEvents.Executing, AuditEvents.BackToPending,
                AuditEvents.Executing, AuditEvents.BackToPending,
                AuditEvents.Executing, AuditEvents.BackToPending,
                AuditEvents.Executing, AuditEvents.Done,
            ],
            (await AuditAsync(card.Id)).Select(r => r.Event));
    }

    [Fact]
    public async Task ThreeWrongPinsEndTheCard()
    {
        await StartAsync();
        var traderBefore = await host.Wallet.AccountBalanceAsync(TraderPhone);
        var card = await CardAsync(U3, TraderTransferWithPin);

        Assert.Equal(new ConfirmOutcome.PinWrong(2), await ConfirmAsync(card.UserId, card.Id, WrongPin));
        Assert.Equal(new ConfirmOutcome.PinWrong(1), await ConfirmAsync(card.UserId, card.Id, WrongPin));
        Assert.Equal(new ConfirmOutcome.NotDone(ConfirmHandler.PinLocked, null), await ConfirmAsync(card.UserId, card.Id, WrongPin));

        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.NotDone, record.State);
        Assert.Equal(ConfirmHandler.PinLocked, record.ResultCode);
        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.NotDone), await ConfirmAsync(card.UserId, card.Id, TraderPin));
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(traderBefore, await host.Wallet.AccountBalanceAsync(TraderPhone));
    }

    [Fact]
    public async Task PinNeverInAuditLog()
    {
        await StartAsync();
        var card = await CardAsync(U3, TraderTransferWithPin);

        Assert.IsType<ConfirmOutcome.PinWrong>(await ConfirmAsync(card.UserId, card.Id, WrongPin));
        Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(card.UserId, card.Id, TraderPin));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var audit = await db.AuditEntries.AsNoTracking().ToListAsync(Token);
        var pending = await db.PendingTransactions.AsNoTracking().ToListAsync(Token);
        Assert.NotEmpty(audit);
        Assert.NotEmpty(pending);

        foreach (var row in audit.Cast<object>().Concat(pending))
        {
            foreach (var property in row.GetType().GetProperties())
            {
                var value = property.GetValue(row);

                // The key is a random GUID, checked by its shape; typed time, ID and row-version columns cannot hold text.
                if (property.Name == nameof(PendingRecord.IdempotencyKey))
                {
                    Assert.Matches("^[0-9a-f]{32}$", (string)value!);
                    continue;
                }

                if (value is null or DateTimeOffset or Guid or byte[])
                    continue;

                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                Assert.False(text.Contains(TraderPin, StringComparison.Ordinal), $"{row.GetType().Name}.{property.Name} holds the PIN.");
                Assert.False(text.Contains(WrongPin, StringComparison.Ordinal), $"{row.GetType().Name}.{property.Name} holds the wrong PIN.");
            }
        }
    }

    [Fact]
    public async Task SettleDuringASlowPinPaymentStaysChecking()
    {
        await StartAsync();
        var traderBefore = await host.Wallet.AccountBalanceAsync(TraderPhone);
        var card = await CardAsync(U3, TraderTransferWithPin);
        Assert.True(card.PinRequired);
        await SetFaultAsync(new FaultRequest("slow", 3_000, Once: true));

        try
        {
            // The client gives up after one second; the wallet is still holding the call (with its PIN) and will
            // finish it.
            var detached = host.CreateClient(new DetachedHandler { InnerHandler = host.Wallet.CreateHandler() }, TimeSpan.FromSeconds(1));
            Assert.IsType<ConfirmOutcome.Checking>(await ConfirmThroughAsync(detached, card, TraderPin));
            Assert.Equal(PendingState.Checking, (await RecordAsync(card.Id)).State);

            // The PIN-less resend is refused pin_required while the key is still unknown: that refusal was never
            // stored, so the record stays Checking (security review I1).
            Assert.IsType<ConfirmOutcome.Checking>(await SettleAsync(card.UserId, card.Id));
            Assert.Equal(PendingState.Checking, (await RecordAsync(card.Id)).State);

            // Once the original call has finished, the resend is a replay of its stored outcome.
            var stored = await WaitUntilDoneAsync(card.IdempotencyKey, U3);
            var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.UserId, card.Id));

            Assert.Equal(stored.Reference, settled.Reference);
            Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
            Assert.Equal(traderBefore - 1_101_000, await host.Wallet.AccountBalanceAsync(TraderPhone));
            var record = await RecordAsync(card.Id);
            Assert.Equal(PendingState.Done, record.State);
            Assert.Equal(stored.Reference, record.Reference);
        }
        finally
        {
            await SetFaultAsync(new FaultRequest("none", null));
        }
    }

    [Fact]
    public async Task PinRefusalAfterTheSettleWindowIsNotDone()
    {
        await StartAsync();
        var traderBefore = await host.Wallet.AccountBalanceAsync(TraderPhone);
        var card = await CardAsync(U3, TraderTransferWithPin);
        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmIntoCheckingAsync(card, TraderPin));

        // Within the window a PIN refusal may still mean "the original is running".
        Assert.IsType<ConfirmOutcome.Checking>(await SettleAsync(card.UserId, card.Id));

        host.Clock.Advance(TimeSpan.FromSeconds(SettleWindowSeconds + 1));
        var outcome = await SettleAsync(card.UserId, card.Id);

        Assert.Equal(new ConfirmOutcome.NotDone(ReasonCodes.NotDoneNothingTaken, "pin_required"), outcome);
        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.NotDone, record.State);
        Assert.Equal("pin_required", record.ResultCode);
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(traderBefore, await host.Wallet.AccountBalanceAsync(TraderPhone));
    }

    [Fact]
    public async Task ExceptionDuringExecuteLeavesChecking()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        var client = StubbedExecuteClient(() => throw new InvalidOperationException("Stub failure during execute."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfirmThroughAsync(client, card));

        Assert.Equal(PendingState.Checking, (await RecordAsync(card.Id)).State);
        var last = (await AuditAsync(card.Id))[^1];
        Assert.Equal(
            (AuditEvents.Checking, "Executing", "Checking", ConfirmHandler.ExceptionReason),
            (last.Event, last.FromState, last.ToState, last.Reason));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task DailyCapReachedAfterTheCardGivesWhatIsLeft()
    {
        await StartAsync();
        var card = await CardAsync(U3, TraderTransferWithPin);

        // U3 pays 4,500,000 twice from outside while the card waits, so the card's 1,100,000 no longer fits today.
        for (var i = 0; i < 2; i++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/transactions")
            {
                Content = JsonContent.Create(new PaymentRequest(U3, "transfer", 4_500_000, null, SeedFacts.DemoAccountId, null, null, TraderPin)),
            };
            message.Headers.Add(WalletClient.IdempotencyKeyHeader, "test-outside-cap-" + i.ToString(CultureInfo.InvariantCulture));
            using var response = await host.Wallet.Client.SendAsync(message, Token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        long spent;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var daily = await scope.ServiceProvider.GetRequiredService<Checks>().DailySpentAsync(U3, Token);
            spent = Assert.IsType<WalletResult<long>.Ok>(daily).Value;
        }

        var perDayCap = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value.PerDayCapIqd;
        Assert.True(spent + card.AmountIqd > perDayCap);

        var outcome = await ConfirmAsync(card.UserId, card.Id, TraderPin);

        Assert.Equal(new ConfirmOutcome.NotDone(ReasonCodes.OverDailyCap, null, Math.Max(0, perDayCap - spent)), outcome);
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
    }

    [Fact]
    public async Task SettleWindowIsAnchoredOnExecutingSince()
    {
        await StartAsync();
        var card = await CardAsync(U3, TraderTransferWithPin);

        // The Confirm comes 40 seconds after the card was created, inside its timer.
        host.Clock.Advance(TimeSpan.FromSeconds(40));
        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmIntoCheckingAsync(card, TraderPin));
        var executingSince = (await RecordAsync(card.Id)).ExecutingSince;
        Assert.Equal<DateTimeOffset?>(card.CreatedAt.AddSeconds(40), executingSince);

        // Past the window counted from the card's creation, but not from the Confirm: still Checking.
        host.Clock.Advance(TimeSpan.FromSeconds(SettleWindowSeconds - 20));
        Assert.IsType<ConfirmOutcome.Checking>(await SettleAsync(card.UserId, card.Id));
        Assert.Equal(PendingState.Checking, (await RecordAsync(card.Id)).State);

        // Past the window counted from the Confirm: the PIN refusal is final.
        host.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(
            new ConfirmOutcome.NotDone(ReasonCodes.NotDoneNothingTaken, "pin_required"),
            await SettleAsync(card.UserId, card.Id));
    }

    [Fact]
    public async Task ExceptionBeforeTheExecuteGoesBackToPending()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        var client = host.CreateClient(
            new ThrowingBalanceHandler { InnerHandler = host.Wallet.CreateHandler() }, TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfirmThroughAsync(client, card));

        // Nothing was sent, so the card stays open with its key and timer.
        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.Pending, record.State);
        Assert.Null(record.ExecutingSince);
        var last = (await AuditAsync(card.Id))[^1];
        Assert.Equal(
            (AuditEvents.BackToPending, "Executing", "Pending", ConfirmHandler.ExceptionReason),
            (last.Event, last.FromState, last.ToState, last.Reason));
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));

        // Another tap on the same card goes through.
        Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(card.UserId, card.Id));
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
    }

    /// <summary>Throws on the balance read (the first call of the recheck) and passes every other request on.</summary>
    private sealed class ThrowingBalanceHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => request.RequestUri?.AbsolutePath.EndsWith("/balance", StringComparison.Ordinal) == true
                ? throw new InvalidOperationException("Stub failure during the recheck.")
                : base.SendAsync(request, cancellationToken);
    }

    [Fact]
    public async Task ConcurrentSettlesSettleOnce()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmIntoCheckingAsync(card));

        var outcomes = await Task.WhenAll(SettleAsync(card.UserId, card.Id), SettleAsync(card.UserId, card.Id));

        Assert.Single(outcomes.OfType<ConfirmOutcome.Done>());
        Assert.Single(outcomes, o => o == new ConfirmOutcome.AlreadyHandled(PendingState.Done));
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Single(await AuditAsync(card.Id), r => r.Event == AuditEvents.Done);
    }

    [Fact]
    public async Task AnotherUsersCardIsNotFound()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);

        Assert.IsType<ConfirmOutcome.NotFound>(await ConfirmAsync(U2, card.Id));
        Assert.IsType<ConfirmOutcome.NotFound>(await SettleAsync(U2, card.Id));
        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.Pending, record.State);
        Assert.Equal(0, record.Attempts);
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));

        // The owner's tap still works.
        Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(U1, card.Id));
    }

    [Fact]
    public async Task WalletRefusalAtExecuteIsNotDone()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        var client = StubbedExecuteClient(() => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(
                """{"status":422,"title":"Insufficient funds","code":"insufficient_funds"}""",
                Encoding.UTF8,
                "application/problem+json"),
        });

        var outcome = await ConfirmThroughAsync(client, card);

        Assert.Equal(new ConfirmOutcome.NotDone(ReasonCodes.InsufficientBalance, null), outcome);
        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.NotDone, record.State);
        Assert.Equal("insufficient_funds", record.ResultCode);
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task BillConfirmPaysTheBiller()
    {
        await StartAsync();
        var card = await CardAsync(U1, ParsedRequest.Bill("water", null, null, AmountUnit.None));
        Assert.Equal("BS3", card.BillCode);

        var done = Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(card.UserId, card.Id));

        Assert.Null(done.PurchaseCode);
        Assert.Equal(SeedFacts.DemoBalance - card.TotalIqd, done.BalanceAfterIqd);
        Assert.Equal(PendingState.Done, (await RecordAsync(card.Id)).State);
        await using var scope = host.Wallet.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var due = await db.BillAccounts.AsNoTracking().Where(b => b.Id == card.BillAccountId).Select(b => b.AmountDueIqd).SingleAsync(Token);
        Assert.Equal(0, due);
    }

    [Fact]
    public async Task PurchaseConfirmReturnsACode()
    {
        await StartAsync();
        var card = await CardAsync(U1, ParsedRequest.Purchase("zain", "credit", 5_000));
        Assert.Equal("ZN-CR-5", card.ProductCode);

        var done = Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(card.UserId, card.Id));

        Assert.NotNull(done.PurchaseCode);
        Assert.Matches("^[0-9]{13}$", done.PurchaseCode);
        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.Done, record.State);
        Assert.Equal(done.PurchaseCode[^4..], record.PurchaseCodeLast4);
        Assert.All(
            await AuditAsync(),
            r => Assert.DoesNotContain(
                done.PurchaseCode,
                string.Join('|', r.Reason, r.Reference, r.Detail, r.Event),
                StringComparison.Ordinal));
    }
}
