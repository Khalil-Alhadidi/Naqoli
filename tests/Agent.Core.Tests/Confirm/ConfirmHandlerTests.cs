using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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
        host.Clock.Now = TimeProvider.System.GetUtcNow();
        await host.ResetAsync();
    }

    private async Task<PendingRecord> CardAsync(int userId, ParsedRequest request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reply = await scope.ServiceProvider.GetRequiredService<Orchestrator>().HandleAsync(userId, request, Token);
        return Assert.IsType<AgentReply.CardIssued>(reply).Pending;
    }

    private async Task<ConfirmOutcome> ConfirmAsync(Guid id, string? pin = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConfirmHandler>().ConfirmAsync(id, pin, Token);
    }

    private async Task<ConfirmOutcome> SettleAsync(Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConfirmHandler>().SettleCheckingAsync(id, Token);
    }

    /// <summary>A Confirm through a handler whose wallet client gives up after one second.</summary>
    private async Task<ConfirmOutcome> ConfirmImpatientlyAsync(Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<ConfirmHandler>(scope.ServiceProvider, host.CreateClient(TimeSpan.FromSeconds(1)));
        return await handler.ConfirmAsync(id, null, Token);
    }

    private async Task<PendingRecord> RecordAsync(Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var record = await scope.ServiceProvider.GetRequiredService<PendingStore>().FindAsync(id, Token);
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

    [Fact]
    public async Task ConfirmMovesMoneyOnce()
    {
        await StartAsync();
        var hammoudiBefore = await host.Wallet.AccountBalanceAsync(SeedFacts.HammoudiPhone);
        var card = await CardAsync(U1, FiftyThousandToHammoudi);

        var outcome = await ConfirmAsync(card.Id);

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
        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.Done), await ConfirmAsync(card.Id));
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task DoubleTapExecutesOnce()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);

        var outcomes = await Task.WhenAll(ConfirmAsync(card.Id), ConfirmAsync(card.Id));

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
        var outcome = await ConfirmAsync(card.Id);

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
            Assert.True(await scope.ServiceProvider.GetRequiredService<PendingStore>().CancelAsync(card.Id, Token));

        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.Cancelled), await ConfirmAsync(card.Id));
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
    }

    [Fact]
    public async Task UnknownIdIsNotFound()
    {
        await StartAsync();
        Assert.IsType<ConfirmOutcome.NotFound>(await ConfirmAsync(Guid.NewGuid()));
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

        var outcome = await ConfirmAsync(card.Id);

        Assert.Equal(new ConfirmOutcome.NotDone(ReasonCodes.InsufficientBalance, null), outcome);
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(8_000, await host.Wallet.AccountBalanceAsync(TightPhone));
        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.NotDone, record.State);
        Assert.Equal(ReasonCodes.InsufficientBalance, record.ResultCode);
    }

    [Fact]
    public async Task WalletErrorIsChecking()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        await SetFaultAsync(new FaultRequest("error", null, Once: true));

        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmAsync(card.Id));

        var checking = await RecordAsync(card.Id);
        Assert.Equal(PendingState.Checking, checking.State);
        Assert.Equal(card.IdempotencyKey, checking.IdempotencyKey);
        Assert.Equal("http_500", (await AuditAsync(card.Id)).Last().Reason);
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));

        // The once fault has cleared itself; the resend under the same key executes now.
        var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.Id));

        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
        var byKey = Assert.IsType<WalletResult<WalletTransaction>.Ok>(await ByKeyAsync(card.IdempotencyKey, U1)).Value;
        Assert.Equal(byKey.Reference, settled.Reference);
        Assert.Equal(699_000, settled.BalanceAfterIqd);
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(PendingState.Done, (await RecordAsync(card.Id)).State);
        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.Done), await SettleAsync(card.Id));
    }

    [Fact]
    public async Task TimeoutAfterExecuteIsCheckingThenDone()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        await SetFaultAsync(new FaultRequest("timeoutAfterExecute", 20_000, Once: true));

        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmImpatientlyAsync(card.Id));
        Assert.Equal(PendingState.Checking, (await RecordAsync(card.Id)).State);
        Assert.Equal(WalletClient.Timeout, (await AuditAsync(card.Id)).Last().Reason);

        // The wallet paid before it went quiet: the key is already done, so the resend is a replay.
        var stored = Assert.IsType<WalletResult<WalletTransaction>.Ok>(await ByKeyAsync(card.IdempotencyKey, U1)).Value;
        Assert.Equal("done", stored.Status);

        var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.Id));

        Assert.Equal(stored.Reference, settled.Reference);
        Assert.Equal(699_000, settled.BalanceAfterIqd);
        Assert.Equal(1, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(699_000, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));
        Assert.Equal(PendingState.Done, (await RecordAsync(card.Id)).State);

        await SetFaultAsync(new FaultRequest("none", null));
    }

    [Fact]
    public async Task TimeoutBeforeExecuteIsCheckingThenDoneByResend()
    {
        await StartAsync();
        var card = await CardAsync(U1, FiftyThousandToHammoudi);
        await SetFaultAsync(new FaultRequest("timeoutBeforeExecute", 2_000, Once: true));

        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmImpatientlyAsync(card.Id));
        Assert.Equal(PendingState.Checking, (await RecordAsync(card.Id)).State);
        Assert.Equal(SeedFacts.DemoBalance, await host.Wallet.AccountBalanceAsync(SeedFacts.DemoPhone));

        // Nothing was written; the fault has cleared, so the resend executes now, once.
        var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.Id));

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
        await SetFaultAsync(new FaultRequest("error", null, Once: true));
        Assert.IsType<ConfirmOutcome.Checking>(await ConfirmAsync(card.Id));

        var settled = Assert.IsType<ConfirmOutcome.Done>(await SettleAsync(card.Id));

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

        Assert.IsType<ConfirmOutcome.PinRequired>(await ConfirmAsync(card.Id));
        Assert.Equal(PendingState.Pending, (await RecordAsync(card.Id)).State);

        Assert.Equal(new ConfirmOutcome.PinWrong(2), await ConfirmAsync(card.Id, WrongPin));
        Assert.Equal(PendingState.Pending, (await RecordAsync(card.Id)).State);

        // A malformed PIN costs no attempt: the next wrong one still leaves one.
        Assert.IsType<ConfirmOutcome.PinMalformed>(await ConfirmAsync(card.Id, "12"));
        Assert.Equal(PendingState.Pending, (await RecordAsync(card.Id)).State);
        Assert.Equal(new ConfirmOutcome.PinWrong(1), await ConfirmAsync(card.Id, WrongPin));

        // The right PIN, typed in Arabic-Indic digits, goes through.
        var done = Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(card.Id, ArabicDigits.ToArabicIndic(TraderPin)));

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

        Assert.Equal(new ConfirmOutcome.PinWrong(2), await ConfirmAsync(card.Id, WrongPin));
        Assert.Equal(new ConfirmOutcome.PinWrong(1), await ConfirmAsync(card.Id, WrongPin));
        Assert.Equal(new ConfirmOutcome.NotDone(ConfirmHandler.PinLocked, null), await ConfirmAsync(card.Id, WrongPin));

        var record = await RecordAsync(card.Id);
        Assert.Equal(PendingState.NotDone, record.State);
        Assert.Equal(ConfirmHandler.PinLocked, record.ResultCode);
        Assert.Equal(new ConfirmOutcome.AlreadyHandled(PendingState.NotDone), await ConfirmAsync(card.Id, TraderPin));
        Assert.Equal(0, await WalletRowsAsync(card.IdempotencyKey));
        Assert.Equal(traderBefore, await host.Wallet.AccountBalanceAsync(TraderPhone));
    }

    [Fact]
    public async Task PinNeverInAuditLog()
    {
        await StartAsync();
        var card = await CardAsync(U3, TraderTransferWithPin);

        Assert.IsType<ConfirmOutcome.PinWrong>(await ConfirmAsync(card.Id, WrongPin));
        Assert.IsType<ConfirmOutcome.Done>(await ConfirmAsync(card.Id, TraderPin));

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
}
