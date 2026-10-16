namespace Agent.Core.Learning;

/// <summary>
/// One row of the learning log (D12, slide 2): a request the agent did not do, for a person to review. It holds the
/// masked text of the message: a run of seven or more digits is a token, and nothing from the wallet (no balance, no
/// purchase code) is ever written. A shorter run the user typed stays as typed, so a PIN typed into the chat (four to
/// six digits) can be in it; the policy for that is open (docs/failure-modes.md, "A PIN typed into the chat").
/// </summary>
public sealed class LearningEntryRow
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public int UserId { get; set; }

    /// <summary>A reason of <see cref="LearningLogRule.Logged"/>, or <see cref="LearningLogRule.ModelRefusal"/>, which the conversation writes and the seed's list does not yet name.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The entry's detail (a code, the typed recipient, or what was asked for), masked.</summary>
    public string? Detail { get; set; }

    /// <summary>The message as the parse call saw it: numbers masked.</summary>
    public string MaskedText { get; set; } = string.Empty;

    /// <summary>Set by the person who reviewed the row.</summary>
    public bool Reviewed { get; set; }
}
