using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent.Core.Pending;

/// <summary>
/// The hosted sweep of the pending records: at start and then every <see cref="AgentOptions.SweepIntervalSeconds"/>, in
/// its own DI scope, it marks Expired every card past its timer (<see cref="PendingStore.ExpireDueAsync"/>) and moves
/// to Checking every record Executing for at least <see cref="AgentOptions.StaleExecutingAfterSeconds"/>
/// (<see cref="PendingStore.MarkStaleExecutingAsync"/>). It never calls the wallet. A failed sweep is logged and the
/// loop goes on; only the host's stop token ends it. <c>AddAgentCore</c> registers it only when the interval is above 0.
/// </summary>
public sealed partial class PendingSweeper : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly AgentOptions options;
    private readonly ILogger<PendingSweeper> logger;

    public PendingSweeper(IServiceScopeFactory scopes, IOptions<AgentOptions> options, ILogger<PendingSweeper> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopes = scopes;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <summary>One sweep: how many cards it expired and how many stale Executing records it moved to Checking.</summary>
    public async Task<(int Expired, int Stale)> SweepOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<PendingStore>();

        var expired = await store.ExpireDueAsync(cancellationToken);
        var stale = await store.MarkStaleExecutingAsync(TimeSpan.FromSeconds(options.StaleExecutingAfterSeconds), cancellationToken);
        LogSwept(logger, expired, stale);
        return (expired, stale);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.SweepIntervalSeconds <= 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.SweepIntervalSeconds));
        do
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The next tick tries again; a record left behind is only later, never lost.
                LogSweepFailed(logger, ex);
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pending sweep: {Expired} expired, {Stale} stale Executing moved to Checking.")]
    private static partial void LogSwept(ILogger logger, int expired, int stale);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pending sweep failed; it runs again on the next tick.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
