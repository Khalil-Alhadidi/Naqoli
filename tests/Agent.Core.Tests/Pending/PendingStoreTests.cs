using Agent.Core.Audit;
using Agent.Core.Data;
using Agent.Core.Pending;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Pending;

/// <summary>
/// The pending store against the agent's test database: the key and timer of a new card, the one-way transitions of
/// architecture section 5 (a record leaves Pending only once, rule 6) and the audit row each transition writes.
/// </summary>
[Collection(AgentCollection.Name)]
public class PendingStoreTests(AgentHost host)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CardDraft Draft(long amount = 50_000) => new(
        UserId: SeedFacts.DemoUserId,
        Type: "transfer",
        AmountIqd: amount,
        FeeIqd: 0,
        TotalIqd: amount,
        QuotedBalanceIqd: 1_000_000,
        ToPhone: "07700000002",
        ToAccountId: 2,
        DisplayName: null,
        RegisteredName: null,
        BillAccountId: null,
        BillCode: null,
        ProductId: null,
        ProductCode: null,
        PinRequired: false,
        FirstTime: false,
        Warning: null);

    private async Task StartAsync()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
    }

    private async Task<T> InScopeAsync<T>(Func<PendingStore, Task<T>> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<PendingStore>());
    }

    private async Task InScopeAsync(Func<PendingStore, Task> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<PendingStore>());
    }

    private async Task<List<AuditEntry>> AuditAsync(Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.AuditEntries.AsNoTracking().Where(a => a.PendingId == id).OrderBy(a => a.Id).ToListAsync(Token);
    }

    private Task<PendingRecord?> FindAsync(Guid id) => InScopeAsync(s => s.FindAsync(id, Token));

    private Task<TransitionResult> BeginAsync(Guid id) => InScopeAsync(s => s.TryBeginExecuteAsync(id, Token));

    [Fact]
    public async Task CreateAssignsKeyAndExpiry()
    {
        await StartAsync();

        var record = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        var stored = await FindAsync(record.Id);

        Assert.NotNull(stored);
        Assert.Matches("^[a-z0-9]{32}$", stored.IdempotencyKey);
        Assert.Equal(TimeSpan.FromSeconds(45), stored.ExpiresAt - stored.CreatedAt);
        Assert.Equal(PendingState.Pending, stored.State);
        Assert.Equal(0, stored.Attempts);
        Assert.Equal(50_000, stored.AmountIqd);
    }

    [Fact]
    public async Task BeginExecuteSucceedsOnce()
    {
        await StartAsync();
        var record = await InScopeAsync(s => s.CreateAsync(Draft(), Token));

        var first = await BeginAsync(record.Id);
        var second = await BeginAsync(record.Id);

        var begun = Assert.IsType<TransitionResult.Begun>(first);
        Assert.Equal(PendingState.Executing, begun.Record.State);
        Assert.Equal(1, begun.Record.Attempts);
        Assert.Equal(new TransitionResult.AlreadyLeft(PendingState.Executing), second);
    }

    [Fact]
    public async Task BeginExecuteOnUnknownIdIsNotFound()
    {
        await StartAsync();

        Assert.IsType<TransitionResult.NotFound>(await BeginAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task BeginExecuteOnExpiredMarksExpired()
    {
        await StartAsync();
        var record = await InScopeAsync(s => s.CreateAsync(Draft(), Token));

        host.Clock.Advance(TimeSpan.FromSeconds(46));
        var result = await BeginAsync(record.Id);

        Assert.IsType<TransitionResult.Expired>(result);
        var stored = await FindAsync(record.Id);
        Assert.Equal(PendingState.Expired, stored!.State);
        Assert.Equal(0, stored.Attempts);
        Assert.NotNull(stored.ClosedAt);
        var last = (await AuditAsync(record.Id))[^1];
        Assert.Equal(AuditEvents.Expired, last.Event);
        Assert.Equal(nameof(PendingState.Pending), last.FromState);
        Assert.Equal(nameof(PendingState.Expired), last.ToState);

        // A second tap on the expired card does nothing.
        Assert.Equal(new TransitionResult.AlreadyLeft(PendingState.Expired), await BeginAsync(record.Id));
    }

    [Fact]
    public async Task CancelledCannotBegin()
    {
        await StartAsync();
        var record = await InScopeAsync(s => s.CreateAsync(Draft(), Token));

        Assert.True(await InScopeAsync(s => s.CancelAsync(record.Id, Token)));
        Assert.False(await InScopeAsync(s => s.CancelAsync(record.Id, Token)));

        Assert.Equal(new TransitionResult.AlreadyLeft(PendingState.Cancelled), await BeginAsync(record.Id));
        var rows = await AuditAsync(record.Id);
        Assert.Equal([AuditEvents.Created, AuditEvents.Cancelled], rows.Select(a => a.Event));
    }

    [Fact]
    public async Task ConcurrentBeginExecuteWinsOnce()
    {
        await StartAsync();
        var record = await InScopeAsync(s => s.CreateAsync(Draft(), Token));

        // Each task has its own scope, because the context is scoped and not thread-safe.
        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => BeginAsync(record.Id), Token)).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Single(results.OfType<TransitionResult.Begun>());
        Assert.All(
            results.Where(r => r is not TransitionResult.Begun),
            r => Assert.Equal(new TransitionResult.AlreadyLeft(PendingState.Executing), r));
        Assert.Equal(1, (await FindAsync(record.Id))!.Attempts);
        Assert.Single(await AuditAsync(record.Id), a => a.Event == AuditEvents.Executing);
    }

    public static TheoryData<PendingState, PendingState, bool> Pairs()
    {
        var allowed = new HashSet<(PendingState, PendingState)>
        {
            (PendingState.Pending, PendingState.Cancelled),
            (PendingState.Pending, PendingState.Expired),
            (PendingState.Pending, PendingState.Executing),
            (PendingState.Executing, PendingState.Done),
            (PendingState.Executing, PendingState.NotDone),
            (PendingState.Executing, PendingState.Checking),
            (PendingState.Executing, PendingState.Pending),
            (PendingState.Checking, PendingState.Done),
            (PendingState.Checking, PendingState.NotDone),
        };
        var data = new TheoryData<PendingState, PendingState, bool>();
        foreach (var from in Enum.GetValues<PendingState>())
        {
            foreach (var to in Enum.GetValues<PendingState>())
                data.Add(from, to, allowed.Contains((from, to)));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TransitionsFollowSectionFive(PendingState from, PendingState to, bool allowed)
    {
        Assert.Equal(allowed, PendingRecord.CanLeave(from, to));
    }

    [Fact]
    public async Task EveryTransitionWritesAnAuditRow()
    {
        await StartAsync();
        var record = await InScopeAsync(s => s.CreateAsync(Draft(), Token));

        Assert.IsType<TransitionResult.Begun>(await BeginAsync(record.Id));
        await InScopeAsync(s => s.MarkCheckingAsync(record.Id, "timeout", Token));
        await InScopeAsync(s => s.CompleteAsync(record.Id, PendingState.Done, null, "TX-1", 950_000, null, Token));

        var rows = await AuditAsync(record.Id);
        Assert.Equal(
            [
                (AuditEvents.Created, (string?)null, (string?)"Pending"),
                (AuditEvents.Executing, "Pending", "Executing"),
                (AuditEvents.Checking, "Executing", "Checking"),
                (AuditEvents.Done, "Checking", "Done"),
            ],
            rows.Select(a => (a.Event, a.FromState, a.ToState)));
        Assert.All(rows, a => Assert.Equal(SeedFacts.DemoUserId, a.UserId));
        Assert.Equal("timeout", rows[2].Reason);
        Assert.Equal("TX-1", rows[3].Reference);

        var stored = await FindAsync(record.Id);
        Assert.Equal(PendingState.Done, stored!.State);
        Assert.Equal("TX-1", stored.Reference);
        Assert.Equal(950_000, stored.BalanceAfterIqd);
        Assert.NotNull(stored.ClosedAt);
    }

    [Fact]
    public async Task BackToPendingKeepsTheCardAndItsTimer()
    {
        await StartAsync();
        var record = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        Assert.IsType<TransitionResult.Begun>(await BeginAsync(record.Id));

        await InScopeAsync(s => s.BackToPendingAsync(record.Id, "wrong_pin", Token));

        var stored = await FindAsync(record.Id);
        Assert.Equal(PendingState.Pending, stored!.State);
        Assert.Equal(record.ExpiresAt, stored.ExpiresAt);
        Assert.Equal(record.IdempotencyKey, stored.IdempotencyKey);
        var last = (await AuditAsync(record.Id))[^1];
        Assert.Equal(
            (AuditEvents.BackToPending, "Executing", "Pending", "wrong_pin"),
            (last.Event, last.FromState, last.ToState, last.Reason));
        Assert.IsType<TransitionResult.Begun>(await BeginAsync(record.Id));
    }

    [Fact]
    public async Task CompleteRefusesARecordThatIsNotExecutingOrChecking()
    {
        await StartAsync();
        var record = await InScopeAsync(s => s.CreateAsync(Draft(), Token));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InScopeAsync(s => s.CompleteAsync(record.Id, PendingState.Done, null, "TX-1", null, null, Token)));
        Assert.Single(await AuditAsync(record.Id));
        Assert.Equal(PendingState.Pending, (await FindAsync(record.Id))!.State);
    }

    [Fact]
    public async Task CompleteStoresAtMostFourDigitsOfAPurchaseCode()
    {
        await StartAsync();
        var record = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        Assert.IsType<TransitionResult.Begun>(await BeginAsync(record.Id));

        await Assert.ThrowsAsync<ArgumentException>(
            () => InScopeAsync(s => s.CompleteAsync(record.Id, PendingState.Done, null, "TX-1", null, "12345", Token)));
        await InScopeAsync(s => s.CompleteAsync(record.Id, PendingState.Done, null, "TX-1", null, "2345", Token));

        Assert.Equal("2345", (await FindAsync(record.Id))!.PurchaseCodeLast4);
    }

    [Fact]
    public async Task ExpireDueExpiresOnlyCardsPastTheirTimer()
    {
        await StartAsync();
        var old = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        host.Clock.Advance(TimeSpan.FromSeconds(30));
        var fresh = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        host.Clock.Advance(TimeSpan.FromSeconds(16));

        Assert.Equal(1, await InScopeAsync(s => s.ExpireDueAsync(Token)));

        Assert.Equal(PendingState.Expired, (await FindAsync(old.Id))!.State);
        Assert.Equal(PendingState.Pending, (await FindAsync(fresh.Id))!.State);
        Assert.Equal(AuditEvents.Expired, (await AuditAsync(old.Id))[^1].Event);
    }

    [Fact]
    public async Task ExpireAllOpenAfterReset()
    {
        await StartAsync();
        var pending = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        var checking = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        var executing = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        Assert.IsType<TransitionResult.Begun>(await BeginAsync(checking.Id));
        await InScopeAsync(s => s.MarkCheckingAsync(checking.Id, "timeout", Token));
        Assert.IsType<TransitionResult.Begun>(await BeginAsync(executing.Id));

        Assert.Equal(2, await InScopeAsync(s => s.ExpireAllOpenAsync("wallet_reset", Token)));

        Assert.Equal(PendingState.Expired, (await FindAsync(pending.Id))!.State);
        var settled = (await FindAsync(checking.Id))!;
        Assert.Equal((PendingState.NotDone, "wallet_reset"), (settled.State, settled.ResultCode));
        Assert.Equal(PendingState.Executing, (await FindAsync(executing.Id))!.State);

        var pendingLast = (await AuditAsync(pending.Id))[^1];
        Assert.Equal(
            (AuditEvents.Expired, "Pending", "Expired", "wallet_reset"),
            (pendingLast.Event, pendingLast.FromState, pendingLast.ToState, pendingLast.Reason));
        var checkingLast = (await AuditAsync(checking.Id))[^1];
        Assert.Equal(
            (AuditEvents.NotDone, "Checking", "NotDone", "wallet_reset"),
            (checkingLast.Event, checkingLast.FromState, checkingLast.ToState, checkingLast.Reason));
    }
}
