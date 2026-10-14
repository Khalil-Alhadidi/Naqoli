using Agent.Core.Trace;

namespace Agent.Web.Services;

/// <summary>One step of the live trace, as the console shows it.</summary>
public sealed record TraceEntry(DateTimeOffset At, TraceZone Zone, string Step, string? Detail);

/// <summary>
/// The live trace of one circuit: the <see cref="ITraceSink"/> the orchestrator and the Confirm handler write to while
/// they serve this circuit's <see cref="ChatSession"/>. Holds codes, states and amounts only (see <c>ITraceSink</c>).
/// </summary>
/// <remarks>
/// Each <see cref="ChatSession"/> action runs in its own DI scope, which would get its own scoped sink. So the session
/// sets <see cref="Current"/> to its circuit's log for the duration of the action, and the scoped <see cref="ITraceSink"/>
/// registration (<c>AddAgentWebServices</c>) returns <see cref="Current"/> when it is set and the scope's own
/// <see cref="TraceLog"/> otherwise. An <see cref="AsyncLocal{T}"/> value set in an async method flows into the calls it
/// awaits and is gone when the method returns, so it never leaks into another circuit's work.
/// </remarks>
public sealed class TraceLog(TimeProvider clock) : ITraceSink
{
    private readonly Lock gate = new();
    private readonly List<TraceEntry> entries = [];

    /// <summary>The log of the circuit whose action is running on this async flow, if any.</summary>
    internal static AsyncLocal<TraceLog?> Current { get; } = new();

    /// <summary>Raised after every new step and after <see cref="Clear"/>; handlers must not block.</summary>
    public event Action? Changed;

    /// <summary>The steps so far, oldest first.</summary>
    public IReadOnlyList<TraceEntry> Entries
    {
        get
        {
            lock (gate)
                return [.. entries];
        }
    }

    public void Record(TraceZone zone, string step, string? detail)
    {
        lock (gate)
            entries.Add(new TraceEntry(clock.GetUtcNow(), zone, step, detail));
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (gate)
            entries.Clear();
        Changed?.Invoke();
    }
}
