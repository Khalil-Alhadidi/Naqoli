namespace Agent.Core.Trace;

/// <summary>The default sink: drops every step. The web app registers its own sink before <c>AddAgentCore</c>.</summary>
public sealed class NullTraceSink : ITraceSink
{
    public void Record(TraceZone zone, string step, string? detail)
    {
    }
}
