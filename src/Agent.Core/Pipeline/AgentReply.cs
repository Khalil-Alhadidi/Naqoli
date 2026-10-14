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
/// </summary>
public sealed record Option(
    string Value,
    string? Display,
    string? RegisteredName,
    DateTimeOffset? LastSentAt,
    long? LastSentAmountIqd,
    long? AmountIqd)
{
    /// <summary>The value of the "a card from the store" option of a <c>bill_or_store</c> question (rule 13).</summary>
    public const string StoreInternet = "store:internet";
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
    /// gets <see cref="View"/> only.
    /// </summary>
    public sealed record CardIssued(PendingRecord Pending) : AgentReply
    {
        /// <summary>The card as the screen shows it, projected from <see cref="Pending"/>.</summary>
        public CardView View => CardView.From(Pending);
    }

    /// <summary>A request the agent does not do; <see cref="Reason"/> is the parsed unsupported reason.</summary>
    public sealed record Unsupported(string Reason) : AgentReply;
}
