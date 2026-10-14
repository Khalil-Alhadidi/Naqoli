namespace Agent.Web.Services;

/// <summary>Who wrote a chat message.</summary>
public enum ChatRole
{
    User,
    Agent,
}

/// <summary>What a chat message is; the phone renders each kind its own way.</summary>
public enum ChatMessageKind
{
    Text,
    Card,
    Question,
    Outcome,
}

/// <summary>A button a message carries while it is the session's latest of its kind.</summary>
public enum ChatAction
{
    None,

    /// <summary>The consent button: gives consent, then re-runs the last request.</summary>
    Consent,

    /// <summary>The "check again" button of a Checking outcome: settles under the same key.</summary>
    CheckAgain,
}

/// <summary>
/// One message in the phone. <see cref="Text"/> is the user's own words, or an agent text already filled from the
/// resource file (rule 4); <see cref="Outcome"/> is the outcome code of an outcome message (<c>done</c>, <c>checking</c>,
/// <c>not_done</c>, <c>expired</c>, <c>cancelled</c>, <c>pin</c>, <c>already_handled</c>).
/// </summary>
public sealed record ChatMessage(int Id, ChatRole Role, string Text, ChatMessageKind Kind, string? Outcome = null, ChatAction Action = ChatAction.None);
