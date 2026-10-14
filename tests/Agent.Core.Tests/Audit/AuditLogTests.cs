using Agent.Core.Audit;
using Agent.Core.Data;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Audit;

/// <summary>The audit log: what one entry holds, and that it never holds a PIN, a balance figure or a purchase code.</summary>
[Collection(AgentCollection.Name)]
public class AuditLogTests(AgentHost host)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BalanceShownCarriesNoFigure()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditLog>()
                .WriteAsync(SeedFacts.DemoUserId, null, AuditEvents.BalanceShown, null, null, null, null, null, Token);
        }

        await using var read = host.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<AgentDbContext>();
        var entry = Assert.Single(await db.AuditEntries.AsNoTracking().ToListAsync(Token));
        Assert.Equal(AuditEvents.BalanceShown, entry.Event);
        Assert.Equal(SeedFacts.DemoUserId, entry.UserId);
        Assert.Equal(host.Clock.GetUtcNow(), entry.At);
        Assert.Null(entry.AmountIqd);
        Assert.Null(entry.PendingId);
        Assert.Null(entry.Reason);
        Assert.Null(entry.Reference);
    }

    [Fact]
    public void WriteHasNoParameterForAPinABalanceOrAPurchaseCode()
    {
        var names = typeof(AuditLog).GetMethod(nameof(AuditLog.WriteAsync))!
            .GetParameters()
            .Select(p => p.Name!.ToLowerInvariant());

        Assert.DoesNotContain(names, n => n.Contains("pin") || n.Contains("balance") || n.Contains("code"));
    }
}
