namespace Agent.Web.Services;

/// <summary>Where the card on the phone stands, as the session last saw it.</summary>
public enum CardStatus
{
    /// <summary>Open: Confirm and Cancel work while the timer runs.</summary>
    Live,
    Done,
    NotDone,

    /// <summary>The wallet's answer is unknown; "check again" settles it. Never shown as failed (rule 6).</summary>
    Checking,
    Expired,
    Cancelled,
}
