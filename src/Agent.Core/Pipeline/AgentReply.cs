using Agent.Core.Aliases;
using Agent.Core.Pending;

namespace Agent.Core.Pipeline;

/// <summary>How the user answers a question.</summary>
public enum QuestionAnswer
{
    /// <summary>By tapping one of the options.</summary>
    Tap,

    /// <summary>By typing the answer (rule 10: a missing amount or recipient).</summary>
    Typed,
}

/// <summary>
/// One option of a question, built by code from the wallet's data (rule 3). <see cref="Value"/> is what a tap sends
/// back: a contact code, bill code, product code, operator code, an amount in dinars, or <see cref="StoreInternet"/>.
/// <see cref="ProductType"/> is the catalogue type of a product option (<c>credit</c> or <c>internet</c>, ruling 3 of
/// milestone 9b: two products of one operator and one price differ only by it); null on every other option.
/// </summary>
public sealed record Option(
    string Value,
    string? Display,
    string? RegisteredName,
    DateTimeOffset? LastSentAt,
    long? LastSentAmountIqd,
    long? AmountIqd,
    string? ProductType = null)
{
    /// <summary>The value of the "a card from the store" option of a <c>bill_or_store</c> question (rule 13).</summary>
    public const string StoreInternet = "store:internet";

    /// <summary>
    /// The final option of a <c>which_contact</c> or <c>did_you_mean</c> question that has options: the person is none
    /// of them (<c>recipient_not_found</c>). Never in a question's <see cref="AgentReply.Question.Options"/>.
    /// </summary>
    public const string NoneOfThese = "none_of_these";

    /// <summary>
    /// The final option of every other question, typed ones included: it clears the pending request
    /// (<c>question_cancelled</c>). Never in a question's <see cref="AgentReply.Question.Options"/>.
    /// </summary>
    public const string CancelQuestion = "cancel_question";
}

/// <summary>
/// What the agent answers one message with. Carries codes and figures only, never text (rule 4): the phone fills a
/// template from its resource file.
/// </summary>
public abstract record AgentReply
{
    private AgentReply()
    {
    }

    /// <summary>A question; <see cref="Kind"/> is one of <see cref="QuestionKinds"/>.</summary>
    public sealed record Question(string Kind, QuestionAnswer Answer, IReadOnlyList<Option> Options) : AgentReply;

    /// <summary>No card; <see cref="Code"/> is one of <see cref="ReasonCodes"/>, with an optional figure.</summary>
    public sealed record Refusal(string Code, string? Detail, long? AmountIqd) : AgentReply;

    /// <summary>The user's own balance (read only, no card).</summary>
    public sealed record BalanceReply(long BalanceIqd) : AgentReply;

    /// <summary>The consent screen replaces everything until the user gives consent.</summary>
    public sealed record ConsentScreen : AgentReply;

    /// <summary>
    /// A confirmation card was issued for <see cref="Pending"/>. The record is for <c>Agent.Core</c> callers; a component
    /// gets <see cref="View"/> only. <see cref="Note"/> is a reason code shown with the card (a known gap:
    /// <see cref="ReasonCodes.AccountChoiceUnavailable"/>), or null. <see cref="Alias"/> is the alias to offer once the
    /// payment is done (rule 18, milestone 9a): set only for a transfer whose recipient a tap picked from a question that
    /// <see cref="AliasRule.Offers"/>; null otherwise. Kept in memory only, never stored on the record.
    /// </summary>
    public sealed record CardIssued(PendingRecord Pending, string? Note = null, AliasOffer? Alias = null) : AgentReply
    {
        /// <summary>The card as the screen shows it, projected from <see cref="Pending"/>.</summary>
        public CardView View => CardView.From(Pending);
    }

    /// <summary>A request the agent does not do; <see cref="Reason"/> is the parsed unsupported reason.</summary>
    public sealed record Unsupported(string Reason) : AgentReply;
}
