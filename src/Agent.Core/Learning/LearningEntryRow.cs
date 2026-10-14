namespace Agent.Core.Learning;

/// <summary>
/// One row of the learning log (D12, slide 2): a request the agent did not do, for a person to review. It holds the
/// masked text of the message, never the typed text, a number, a PIN, a balance or a purchase code.
/// </summary>
public sealed class LearningEntryRow
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public int UserId { get; set; }

    /// <summary>A reason of <see cref="LearningLogRule.Logged"/>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The entry's detail (a code, the typed recipient, or what was asked for), masked.</summary>
    public string? Detail { get; set; }

    /// <summary>The message as the parse call saw it: numbers masked.</summary>
    public string MaskedText { get; set; } = string.Empty;

    /// <summary>Set by the person who reviewed the row.</summary>
    public bool Reviewed { get; set; }
}
