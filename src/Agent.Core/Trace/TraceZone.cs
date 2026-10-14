namespace Agent.Core.Trace;

/// <summary>Where a step of the live trace happened (architecture section 2: the zones of the page's trace).</summary>
public enum TraceZone
{
    /// <summary>What the user did: a message typed, a tap.</summary>
    User,

    /// <summary>Code that reads, resolves and checks; it never moves money.</summary>
    Code,

    /// <summary>The parse call (read only, rule 2).</summary>
    Model,

    /// <summary>The Confirm handler: the only place money moves (rule 1).</summary>
    Write,
}
