using Agent.Core.Pipeline;
using Agent.Core.Requests;

namespace Agent.Core.Conversation;

/// <summary>
/// What the last rise of <see cref="Conversation.WalletVersion"/> changed in the wallet. The web console's session reads
/// it beside the version to say which kind of wallet change to announce (<c>Agent.Core</c> does not reference the web
/// project, so the kind is named here and mapped there).
/// </summary>
public enum ConversationWalletChange
{
    /// <summary>The version has not gone up yet.</summary>
    None,

    /// <summary>A Done outcome: money moved.</summary>
    Payment,

    /// <summary>An alias was saved (the offer's Save, the directory) or deleted (the directory).</summary>
    Alias,
}

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

    /// <summary>
    /// The offer to save an alias under a Done outcome (rule 18, milestone 9a); no code: the text is
    /// <see cref="Conversation.AliasOffer"/>, read by the screen and the runner.
    /// </summary>
    AliasOffer,

    /// <summary>The offered alias was saved on the user's tap (milestone 9a); no code.</summary>
    AliasSaved,
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
/// <param name="Code">
/// The reason code, question kind, outcome code or option value; null for a card, the consent screen, a balance and a
/// typed message, except a typed message that may be a PIN, which is <see cref="PinMasked"/>.
/// </param>
/// <param name="AmountIqd">The figure: a balance, a refusal's figure (the minimum, a cap, a shortfall), or the balance after a Done outcome.</param>
/// <param name="Reference">The wallet reference of a Done outcome.</param>
/// <param name="PurchaseCode">The purchase code of a Done purchase (shown on the phone only, never traced).</param>
/// <param name="AttemptsLeft">The PIN attempts left of a <c>pin_wrong</c> outcome.</param>
/// <param name="Reason">The reason code of a <c>not_done</c> outcome.</param>
/// <param name="QuestionKind">
/// The kind of the open question a <c>not_understood</c> refusal repeats; the question stays open.
/// </param>
/// <param name="MaskedDigits">
/// The number of digits of a typed message marked <see cref="PinMasked"/>; the digits themselves are never kept.
/// </param>
/// <param name="NotPrepared">
/// The parts of a message beyond <c>Agent:MaxRequestsPerMessage</c>, on the <see cref="ConversationKind.Info"/> message
/// <c>max_three_requests</c> (rule 22, milestone 9c), in the order spoken; null on every other message.
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
    string? QuestionKind = null,
    int? MaskedDigits = null,
    IReadOnlyList<NotPreparedPart>? NotPrepared = null)
{
    /// <summary>
    /// The <see cref="Code"/> of a typed message of four to six digits sent while a card with a PIN field is live: the
    /// phone shows one dot per digit (<see cref="MaskedDigits"/>), never the digits (Khalil, 19 Oct 2026).
    /// </summary>
    public const string PinMasked = "pin_masked";

    /// <summary>
    /// Keeps the purchase code (security review M2 of milestone 9b) and the user's words for the parts not prepared
    /// (milestone 9c; only their count) out of any string form of the message.
    /// </summary>
    public override string ToString()
        => FormattableString.Invariant(
            $"ConversationMessage {{ Role = {Role}, Kind = {Kind}, Code = {Code}, AmountIqd = {AmountIqd}, Reference = {Reference}, PurchaseCode = {(PurchaseCode is null ? "none" : "***")}, AttemptsLeft = {AttemptsLeft}, Reason = {Reason}, QuestionKind = {QuestionKind}, MaskedDigits = {MaskedDigits}, NotPrepared = {(NotPrepared is null ? "none" : NotPrepared.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))} }}");
}

/// <summary>
/// A part of a message beyond <c>Agent:MaxRequestsPerMessage</c> (rule 22, milestone 9c): nothing is prepared for it,
/// and the reply names it.
/// </summary>
/// <param name="Intent">The part's request type; the phone names a part with no text by its type's word.</param>
/// <param name="Text">
/// The user's own words for the part (a transfer's recipient, a bill's biller or label, a purchase's operator), or null.
/// The parser's text counts only when the typed message holds it as written or in its lam form
/// (<see cref="Text.ArabicText.LamForm"/>; ruling 2 as amended, 19 Oct 2026), so no model text reaches the phone.
/// </param>
public sealed record NotPreparedPart(RequestIntent Intent, string? Text);

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
