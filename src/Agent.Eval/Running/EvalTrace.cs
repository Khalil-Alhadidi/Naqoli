using Agent.Core.Trace;

namespace Agent.Eval.Running;

/// <summary>One trace step the runner kept.</summary>
public sealed record EvalTraceEntry(TraceZone Zone, string Step, string? Detail);

/// <summary>
/// The runner's <see cref="ITraceSink"/>: keeps the steps of the case being played, so the runner can check that no PIN
/// reaches the trace. Cleared before every case. Holds codes, states and amounts only, as every sink does.
/// </summary>
public sealed class EvalTrace : ITraceSink
{
    private readonly Lock gate = new();
    private readonly List<EvalTraceEntry> entries = [];

    /// <summary>The steps since the last <see cref="Clear"/>, oldest first.</summary>
    public IReadOnlyList<EvalTraceEntry> Entries
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
            entries.Add(new EvalTraceEntry(zone, step, detail));
    }

    public void Clear()
    {
        lock (gate)
            entries.Clear();
    }
}
