using Agent.Core.Parsing;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The pacer over a hand-moved clock (final review M8): a zero gap never waits; callers queued on one candidate start a
/// gap apart; a caller cancelled while queued, or while waiting out the gap, leaves the gate usable and records no start.
/// Time moves only when a test says so, so no assertion depends on the machine's speed.
/// </summary>
public class ParsePacerTests
{
    private const string Candidate = "gemini";
    private const int GapMs = 1_000;

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AZeroGapReturnsAtOnce(int gap)
    {
        var clock = new ManualClock();
        var pacer = new ParsePacer(clock);

        Assert.True(pacer.WaitAsync(Candidate, gap, TestContext.Current.CancellationToken).IsCompletedSuccessfully);
        Assert.True(pacer.WaitAsync(Candidate, gap, TestContext.Current.CancellationToken).IsCompletedSuccessfully);
        Assert.Equal(0, clock.PendingTimers);
    }

    [Fact]
    public async Task ThreeCallersOnOneCandidateStartAGapApart()
    {
        var clock = new ManualClock();
        var pacer = new ParsePacer(clock);

        var first = pacer.WaitAsync(Candidate, GapMs, TestContext.Current.CancellationToken);
        Assert.True(first.IsCompletedSuccessfully);
        var second = pacer.WaitAsync(Candidate, GapMs, TestContext.Current.CancellationToken);
        var third = pacer.WaitAsync(Candidate, GapMs, TestContext.Current.CancellationToken);
        await clock.WaitForTimersAsync(1, Settle);

        clock.Advance(TimeSpan.FromMilliseconds(GapMs - 1));
        Assert.False(second.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await second.WaitAsync(Settle, TestContext.Current.CancellationToken);
        Assert.False(third.IsCompleted);

        // The third waits a whole gap from the second's start, not from the first's.
        await clock.WaitForTimersAsync(1, Settle);
        clock.Advance(TimeSpan.FromMilliseconds(GapMs - 1));
        Assert.False(third.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await third.WaitAsync(Settle, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ACallerCancelledWhileQueuedLeavesTheGateUsable()
    {
        var clock = new ManualClock();
        var pacer = new ParsePacer(clock);
        await pacer.WaitAsync(Candidate, GapMs, TestContext.Current.CancellationToken);
        var waiting = pacer.WaitAsync(Candidate, GapMs, TestContext.Current.CancellationToken);
        await clock.WaitForTimersAsync(1, Settle);
        using var cancel = new CancellationTokenSource();
        var queued = pacer.WaitAsync(Candidate, GapMs, cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        clock.Advance(TimeSpan.FromMilliseconds(GapMs));
        await waiting.WaitAsync(Settle, TestContext.Current.CancellationToken);
        var next = pacer.WaitAsync(Candidate, GapMs, TestContext.Current.CancellationToken);
        await clock.WaitForTimersAsync(1, Settle);
        clock.Advance(TimeSpan.FromMilliseconds(GapMs));
        await next.WaitAsync(Settle, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ACallerCancelledInTheDelayRecordsNoStart()
    {
        var clock = new ManualClock();
        var pacer = new ParsePacer(clock);
        await pacer.WaitAsync(Candidate, GapMs, TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource();
        var cancelled = pacer.WaitAsync(Candidate, GapMs, cancel.Token);
        await clock.WaitForTimersAsync(1, Settle);
        clock.Advance(TimeSpan.FromMilliseconds(GapMs / 2));

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        // The next caller waits only what is left of the gap since the first start: the cancelled one recorded none.
        var next = pacer.WaitAsync(Candidate, GapMs, TestContext.Current.CancellationToken);
        await clock.WaitForTimersAsync(1, Settle);
        clock.Advance(TimeSpan.FromMilliseconds((GapMs / 2) - 1));
        Assert.False(next.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await next.WaitAsync(Settle, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EachCandidateHasItsOwnGap()
    {
        var clock = new ManualClock();
        var pacer = new ParsePacer(clock);
        await pacer.WaitAsync(Candidate, GapMs, TestContext.Current.CancellationToken);

        Assert.True(pacer.WaitAsync("openrouter", GapMs, TestContext.Current.CancellationToken).IsCompletedSuccessfully);
    }

    /// <summary>
    /// A clock that moves only on <see cref="Advance"/>, with one-shot timers (what <c>Task.Delay</c> asks of a
    /// <see cref="TimeProvider"/>) that fire when it passes their due time.
    /// </summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> timers = [];
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>The timers armed and not yet fired or disposed.</summary>
        public int PendingTimers
        {
            get
            {
                lock (gate)
                    return timers.Count(t => t.Due is not null);
            }
        }

        public override long GetTimestamp()
        {
            lock (gate)
                return ticks;
        }

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (gate)
                timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>Moves the clock on and fires every timer now due, outside the lock.</summary>
        public void Advance(TimeSpan by)
        {
            List<ManualTimer> due;
            lock (gate)
            {
                ticks += by.Ticks;
                due = [.. timers.Where(t => t.Due is { } d && d <= ticks)];
                foreach (var timer in due)
                    timer.Due = null;
            }

            foreach (var timer in due)
                timer.Fire();
        }

        /// <summary>Waits (in real time, at most <paramref name="limit"/>) until <paramref name="count"/> timers are armed.</summary>
        public async Task WaitForTimersAsync(int count, TimeSpan limit)
        {
            var until = DateTime.UtcNow + limit;
            while (PendingTimers < count)
            {
                if (DateTime.UtcNow > until)
                    throw new TimeoutException(FormattableString.Invariant($"{PendingTimers} timer(s) armed, {count} expected."));
                await Task.Delay(5);
            }
        }

        private bool Arm(ManualTimer timer, TimeSpan dueTime)
        {
            lock (gate)
            {
                if (!timers.Contains(timer))
                    return false;
                timer.Due = dueTime == Timeout.InfiniteTimeSpan ? null : ticks + Math.Max(0, dueTime.Ticks);
                return timer.Due <= ticks;
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (gate)
                timers.Remove(timer);
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public long? Due { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                // Task.Delay asks for a one-shot timer; a due time already passed fires at once.
                if (clock.Arm(this, dueTime))
                {
                    Due = null;
                    Fire();
                }

                return true;
            }

            public void Fire() => callback(state);

            public void Dispose() => clock.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
