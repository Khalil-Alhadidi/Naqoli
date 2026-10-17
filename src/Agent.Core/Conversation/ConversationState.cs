using Agent.Core.Pipeline;

namespace Agent.Core.Conversation;

/// <summary>Who a conversation message is from.</summary>
public enum ConversationRole
{
    User,
    Agent,
}

/// <summary>
/// What a conversation message is. A message carries codes and figures only (rule 4): the screen fills a template from
/// its resource file, and the runner grades the codes.
/// </summary>
public enum ConversationKind
{
    /// <summary>A message the user typed. The text stays with the caller (the screen, the runner).</summary>
    Typed,

    /// <summary>A tap on an option; <see cref="ConversationMessage.Code"/> is the option's value.</summary>
    Option,

    /// <summary>A refusal or informational reply; <see cref="ConversationMessage.Code"/> is one of <see cref="ReasonCodes"/>.</summary>
    Refusal,

    /// <summary>A request the agent does not do; <see cref="ConversationMessage.Code"/> is the parsed reason.</summary>
    Unsupported,

    /// <summary>A question; <see cref="ConversationMessage.Code"/> is one of <see cref="QuestionKinds"/>.</summary>
    Question,

    /// <summary>The user's balance in <see cref="ConversationMessage.AmountIqd"/>.</summary>
    Balance,

    /// <summary>The consent screen, with its button.</summary>
    Consent,

    /// <summary>A confirmation card was issued (<see cref="Conversation.Card"/>).</summary>
    Card,

    /// <summary>
    /// What became of the card; <see cref="ConversationMessage.Code"/> is one of <see cref="OutcomeCodes"/>.
    /// </summary>
    Outcome,

    /// <summary>A note that is not a reply to a request; <see cref="ConversationMessage.Code"/> is <c>points_to_confirm</c>.</summary>
    Info,
}

/// <summary>The codes of <see cref="ConversationKind.Outcome"/> messages.</summary>
public static class OutcomeCodes
{
    public const string Done = "done";
    public const string Checking = "checking";
    public const string NotDone = "not_done";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
    public const string PinRequired = "pin_required";
    public const string PinWrong = "pin_wrong";
    public const string PinMalformed = "pin_malformed";
    public const string AlreadyHandled = "already_handled";
    public const string NotFound = "not_found";
}

/// <summary>
/// One message of a conversation, as codes and figures. A user message holds no text (the caller keeps what was typed);
/// an agent message holds the code of its template and the figures that fill it.
/// </summary>
/// <param name="Role">Who it is from.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Code">The reason code, question kind, outcome code or option value; null for a typed message, a card, the consent screen and a balance.</param>
/// <param name="AmountIqd">The figure: a balance, a refusal's figure (the minimum, a cap, a shortfall), or the balance after a Done outcome.</param>
/// <param name="Reference">The wallet reference of a Done outcome.</param>
/// <param name="PurchaseCode">The purchase code of a Done purchase (shown on the phone only, never traced).</param>
/// <param name="AttemptsLeft">The PIN attempts left of a <c>pin_wrong</c> outcome.</param>
/// <param name="Reason">The reason code of a <c>not_done</c> outcome.</param>
/// <param name="QuestionKind">
/// The kind of the open question a <c>not_understood</c> refusal repeats; the question stays open.
/// </param>
public sealed record ConversationMessage(
    ConversationRole Role,
    ConversationKind Kind,
    string? Code = null,
    long? AmountIqd = null,
    string? Reference = null,
    string? PurchaseCode = null,
    int? AttemptsLeft = null,
    string? Reason = null,
    string? QuestionKind = null);

/// <summary>A question the conversation asked, for the grader: its kind, how it is answered, and the values offered.</summary>
public sealed record ConversationQuestion(string Kind, QuestionAnswer Answer, IReadOnlyList<string> OptionValues);

/// <summary>Where the card stands, as the conversation last saw it.</summary>
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
