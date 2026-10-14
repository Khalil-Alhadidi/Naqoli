using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent.Core.Pending;

/// <summary>
/// The hosted sweep of the pending records: at start and then every <see cref="AgentOptions.SweepIntervalSeconds"/>, in
/// its own DI scope, it marks Expired every card past its timer (<see cref="PendingStore.ExpireDueAsync"/>) and moves
/// to Checking every record Executing for at least <see cref="AgentOptions.StaleExecutingAfterSeconds"/>
/// (<see cref="PendingStore.MarkStaleExecutingAsync"/>). It never calls the wallet. A failed sweep is logged (Warning
/// once per outage, then Debug, Information on recovery) and the loop goes on; only the host's stop token ends it. <c>AddAgentCore</c> registers it only when the interval is above 0.
/// </summary>
public sealed partial class PendingSweeper : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly AgentOptions options;
    private readonly ILogger<PendingSweeper> logger;

    // True from a failed sweep until the next sweep that succeeds; only the loop touches it.
    private bool failing;

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
            if (!await SweepAndLogAsync(stoppingToken))
                return;
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    /// <summary>
    /// One tick of the loop: a sweep that never throws. A failure is logged at Warning once per outage (the first after
    /// a success), at Debug while the outage lasts, and the recovery at Information, so a database that is down does not
    /// fill the log. Returns false only when the host is stopping.
    /// </summary>
    internal async Task<bool> SweepAndLogAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepOnceAsync(stoppingToken);
            if (failing)
            {
                failing = false;
                LogSweepRecovered(logger);
            }

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            // The next tick tries again; a record left behind is only later, never lost.
            if (failing)
            {
                LogSweepStillFailing(logger, ex);
            }
            else
            {
                failing = true;
                LogSweepFailed(logger, ex);
            }

            return true;
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pending sweep failed; it runs again on every tick, logged at Debug until it recovers.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pending sweep still failing.")]
    private static partial void LogSweepStillFailing(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pending sweep recovered.")]
    private static partial void LogSweepRecovered(ILogger logger);
}
