namespace Agent.Eval.Running;

/// <summary>
/// The agent's clock in the runner: real time until a case waits out a card's timer, when <see cref="Advance"/> moves
/// it forward. <see cref="Reset"/> sets it back to the real time before every case, so the Iraq-date daily cap is right.
/// </summary>
public sealed class EvalClock : TimeProvider
{
    private readonly Lock gate = new();
    private DateTimeOffset now = System.GetUtcNow();

    /// <summary>The clock's time.</summary>
    public DateTimeOffset Now
    {
        get
        {
            lock (gate)
                return now;
        }
    }

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>Moves the clock forward by <paramref name="by"/> (zero or more).</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        lock (gate)
            now += by;
    }

    /// <summary>Sets the clock back to the real time.</summary>
    public void Reset()
    {
        lock (gate)
            now = System.GetUtcNow();
    }
}
