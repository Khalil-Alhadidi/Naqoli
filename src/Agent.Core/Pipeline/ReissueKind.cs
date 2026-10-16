namespace Agent.Core.Pipeline;

/// <summary>
/// The two ways a card is rebuilt from its stored request (D19), with a new pending ID, key and timer and fresh checks.
/// </summary>
public enum ReissueKind
{
    /// <summary>"Try again": the card expired, was cancelled or definitely failed; nothing moved.</summary>
    TryAgain,

    /// <summary>"Send again": the payment is done; the new card carries the repeat line.</summary>
    SendAgain,
}
