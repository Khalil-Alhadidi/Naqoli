using Agent.Core.Pending;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agent.Core.Tests.Pending;

/// <summary>
/// The hosted sweep: cards past their timer become Expired, records Executing for longer than
/// <see cref="AgentOptions.StaleExecutingAfterSeconds"/> become Checking, and a failing sweep never stops the loop.
/// </summary>
[Collection(AgentCollection.Name)]
public class PendingSweeperTests(AgentHost host)
{
    private const int User = SeedFacts.DemoUserId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CardDraft Draft() => new(
        UserId: User,
        Type: "transfer",
        AmountIqd: 50_000,
        FeeIqd: 0,
        TotalIqd: 50_000,
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

    private async Task<T> InScopeAsync<T>(Func<PendingStore, Task<T>> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<PendingStore>());
    }

    private PendingSweeper Sweeper() => new(
        host.Services.GetRequiredService<IServiceScopeFactory>(),
        host.Services.GetRequiredService<IOptions<AgentOptions>>(),
        NullLogger<PendingSweeper>.Instance);

    [Fact]
    public async Task OneSweepExpiresCardsAndMovesStaleExecutingToChecking()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        await host.ResetAsync();
        var options = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;

        var open = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        var stale = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        Assert.IsType<TransitionResult.Begun>(await InScopeAsync(s => s.TryBeginExecuteAsync(stale.Id, User, Token)));

        // Past the card timer and the stale-Executing threshold.
        host.Clock.Advance(TimeSpan.FromSeconds(Math.Max(options.CardTimerSeconds, options.StaleExecutingAfterSeconds) + 1));
        var fresh = await InScopeAsync(s => s.CreateAsync(Draft(), Token));
        Assert.IsType<TransitionResult.Begun>(await InScopeAsync(s => s.TryBeginExecuteAsync(fresh.Id, User, Token)));

        using var sweeper = Sweeper();
        Assert.Equal((1, 1), await sweeper.SweepOnceAsync(Token));

        Assert.Equal(PendingState.Expired, (await InScopeAsync(s => s.FindAsync(open.Id, User, Token)))!.State);
        Assert.Equal(PendingState.Checking, (await InScopeAsync(s => s.FindAsync(stale.Id, User, Token)))!.State);
        Assert.Equal(PendingState.Executing, (await InScopeAsync(s => s.FindAsync(fresh.Id, User, Token)))!.State);

        // A second sweep finds nothing more.
        Assert.Equal((0, 0), await sweeper.SweepOnceAsync(Token));
    }

    [Fact]
    public async Task AFailingSweepDoesNotStopTheLoop()
    {
        var factory = new ThrowingScopeFactory();
        var options = Options.Create(new AgentOptions { SweepIntervalSeconds = 1 });
        using var sweeper = new PendingSweeper(factory, options, NullLogger<PendingSweeper>.Instance);

        await sweeper.StartAsync(Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (factory.Calls < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        await sweeper.StopAsync(Token);

        Assert.True(factory.Calls >= 2, $"The sweep ran {factory.Calls} time(s); it should go on after a failure.");
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("5", true)]
    [InlineData(null, true)]
    public void TheSweeperIsHostedOnlyWithAnInterval(string? interval, bool hosted)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:WalletBaseUrl"] = "http://localhost",
                ["Agent:SweepIntervalSeconds"] = interval,
            })
            .Build();
        var services = new ServiceCollection();

        services.AddAgentCore(configuration);

        Assert.Equal(
            hosted,
            services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PendingSweeper)));
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("Stub failure opening a scope.");
        }
    }
}
