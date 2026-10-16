using Agent.Core.Audit;
using Agent.Core.Pipeline;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Audit;

/// <summary>The audit log's read for the console's log component: the newest rows first, as they were written.</summary>
[Collection(AgentCollection.Name)]
public class AuditLogReadTests(AgentHost host)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadLatestIsNewestFirstAndTheBalanceRowHasNoFigure()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
        var pending = Guid.NewGuid();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
            await audit.WriteAsync(SeedFacts.DemoUserId, null, AuditEvents.BalanceShown, null, null, null, null, null, Token);
            host.Clock.Advance(TimeSpan.FromSeconds(1));
            await audit.WriteRefusalAsync(SeedFacts.DemoUserId, ReasonCodes.NotDoneNothingTaken, ReasonCodes.KillSwitchDetail, Token);

            // Two rows at the same moment: the later one (by Id) comes first.
            host.Clock.Advance(TimeSpan.FromSeconds(1));
            await audit.WriteAsync(SeedFacts.DemoUserId, pending, AuditEvents.Created, null, "Pending", null, null, 50_000, Token);
            await audit.WriteAsync(SeedFacts.DemoUserId, pending, AuditEvents.Cancelled, "Pending", "Cancelled", null, null, null, Token);
        }

        IReadOnlyList<AuditEntry> rows;
        await using (var scope = host.Services.CreateAsyncScope())
            rows = await scope.ServiceProvider.GetRequiredService<AuditLog>().ReadLatestAsync(10, Token);

        Assert.Equal(
            [AuditEvents.Cancelled, AuditEvents.Created, AuditEvents.Refused, AuditEvents.BalanceShown],
            rows.Select(r => r.Event));
        Assert.Equal(50_000, rows[1].AmountIqd);
        Assert.Equal(ReasonCodes.KillSwitchDetail, rows[2].Detail);
        var balance = rows[3];
        Assert.Null(balance.AmountIqd);
        Assert.Null(balance.Detail);

        // The page size takes the newest rows only.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var latest = await scope.ServiceProvider.GetRequiredService<AuditLog>().ReadLatestAsync(2, Token);
            Assert.Equal([AuditEvents.Cancelled, AuditEvents.Created], latest.Select(r => r.Event));
        }
    }

    [Fact]
    public async Task ReadLatestRefusesASizeBelowOne()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await using var scope = host.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => scope.ServiceProvider.GetRequiredService<AuditLog>().ReadLatestAsync(0, Token));
    }
}
