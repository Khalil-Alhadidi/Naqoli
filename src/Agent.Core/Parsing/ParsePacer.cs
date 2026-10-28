using System.Collections.Concurrent;

namespace Agent.Core.Parsing;

/// <summary>
/// Keeps the starts of two parse calls on one candidate at least that candidate's
/// <see cref="ModelCandidate.MinMillisecondsBetweenCalls"/> apart, for a vendor whose free tier allows only so many
/// requests a minute. One instance (a singleton) serves every parser, so the pace holds across conversations; each
/// candidate name has its own gate and its own last start.
/// </summary>
public sealed class ParsePacer(TimeProvider clock)
{
    /// <summary>The last start of a candidate that has had no call yet.</summary>
    private const long NoCall = long.MinValue;

    private readonly TimeProvider clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ConcurrentDictionary<string, (SemaphoreSlim Gate, long LastStart)> state = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns at once when <paramref name="minMilliseconds"/> is 0 or below. Otherwise waits, one caller at a time per
    /// <paramref name="candidateName"/>, until that long has passed since the candidate's last call started, then records
    /// now as its last start.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while waiting.</exception>
    public async Task WaitAsync(string candidateName, int minMilliseconds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateName);
        if (minMilliseconds <= 0)
            return;

        var gap = TimeSpan.FromMilliseconds(minMilliseconds);
        var gate = state.GetOrAdd(candidateName, _ => (new SemaphoreSlim(1, 1), NoCall)).Gate;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var last = state[candidateName].LastStart;
            if (last != NoCall)
            {
                // A timer can fire a little early against the clock's own reading, so wait again for what is left.
                for (var remaining = gap - clock.GetElapsedTime(last); remaining > TimeSpan.Zero; remaining = gap - clock.GetElapsedTime(last))
                    await Task.Delay(remaining, clock, cancellationToken).ConfigureAwait(false);
            }

            state[candidateName] = (gate, clock.GetTimestamp());
        }
        finally
        {
            gate.Release();
        }
    }
}
