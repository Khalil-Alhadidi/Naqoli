using Agent.Core.Audit;
using Agent.Core.Data;
using Agent.Core.Pipeline;
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

    [Theory]
    [InlineData(nameof(AuditLog.WriteAsync))]
    [InlineData(nameof(AuditLog.WriteRefusalAsync))]
    public void WriteHasNoParameterForAPinABalanceOrAPurchaseCode(string method)
    {
        var names = typeof(AuditLog).GetMethod(method)!
            .GetParameters()
            .Select(p => p.Name!.ToLowerInvariant());

        Assert.DoesNotContain(names, n => n.Contains("pin") || n.Contains("balance") || n.Contains("code"));
    }

    [Fact]
    public async Task RefusalKeepsTheDetailOnlyWhenNothingWasTaken()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
            await audit.WriteRefusalAsync(SeedFacts.DemoUserId, ReasonCodes.NotDoneNothingTaken, "wallet_timeout", Token);
            await audit.WriteRefusalAsync(SeedFacts.DemoUserId, ReasonCodes.RecipientNotFound, "typed words", Token);
        }

        await using var read = host.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<AgentDbContext>();
        var rows = await db.AuditEntries.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Token);
        Assert.Equal(
            [
                (AuditEvents.Refused, (string?)ReasonCodes.NotDoneNothingTaken, (string?)"wallet_timeout"),
                (AuditEvents.Refused, ReasonCodes.RecipientNotFound, null),
            ],
            rows.Select(r => (r.Event, r.Reason, r.Detail)));
    }
}
