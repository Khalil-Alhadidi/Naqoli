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

    /// <summary>The Save button of the alias offer under a Done outcome (rule 18, milestone 9a).</summary>
    SaveAlias,

    /// <summary>The copy button under a Done purchase's code (milestone 9b); copies <see cref="ChatMessage.CopyText"/>.</summary>
    CopyCode,
}

/// <summary>
/// One message in the phone. <see cref="Text"/> is the user's own words, or an agent text already filled from the
/// resource file (rule 4); <see cref="Outcome"/> is the outcome code of an outcome message (<c>done</c>, <c>checking</c>,
/// <c>not_done</c>, <c>expired</c>, <c>cancelled</c>, <c>pin</c>, <c>already_handled</c>). <see cref="CopyText"/> is what
/// the <see cref="ChatAction.CopyCode"/> button copies: the purchase code as the wallet gave it, in memory only, never
/// logged or traced; null on every other message.
/// </summary>
public sealed record ChatMessage(
    int Id, ChatRole Role, string Text, ChatMessageKind Kind, string? Outcome = null, ChatAction Action = ChatAction.None, string? CopyText = null)
{
    /// <summary>
    /// Keeps the copied code out of any string form of the message, and leaves out <see cref="Text"/>, which is phone text
    /// (the code message holds the code too) (security review M2 of milestone 9b).
    /// </summary>
    public override string ToString()
        => FormattableString.Invariant(
            $"ChatMessage {{ Id = {Id}, Role = {Role}, Kind = {Kind}, Outcome = {Outcome}, Action = {Action}, CopyText = {(CopyText is null ? "none" : "***")} }}");
}
