using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Eval.Cases;

// The case format of data/README.md ("A case", "The answer key"), one record per JSON object. Names are snake_case on
// the wire. Every record refuses a key it does not know (CaseLoader sets UnmappedMemberHandling.Disallow), so a typo in
// a case file is an error, never a silently ignored field. The answer keys are Khalil's: the runner reads them and
// never writes them.

/// <summary>One case of a request set.</summary>
/// <param name="Id">The case id (for example <c>CLN-01</c>, or <c>CLN-03-v1</c> for a variant).</param>
/// <param name="Set"><c>dev</c> or <c>heldout</c>.</param>
/// <param name="Category">The test-set category (Clean, Ambiguous, ...).</param>
/// <param name="Type">The request type (Transfer, Bill, ...).</param>
/// <param name="User">The seed user code (<c>U1</c> to <c>U5</c>).</param>
/// <param name="Message">The first message; never printed or reported.</param>
/// <param name="Steps">What the runner plays, in order.</param>
/// <param name="Fault">The fault injected for this case, or null.</param>
/// <param name="Expect">The answer key.</param>
/// <param name="Parent">The case a variant rewords, or null.</param>
/// <param name="PendingWordingRecheck">True while Khalil re-checks the wording.</param>
/// <param name="Meaning">What the case means, for people; never reported.</param>
/// <param name="Notes">Notes for people; never reported.</param>
public sealed record EvalCase(
    string Id,
    string Set,
    string Category,
    string Type,
    string User,
    string Message,
    IReadOnlyList<CaseStep> Steps,
    CaseFault? Fault,
    Expectation Expect,
    string? Parent = null,
    bool PendingWordingRecheck = false,
    string? Meaning = null,
    string? Notes = null);

/// <summary>
/// One step. Exactly one of <see cref="Say"/>, <see cref="Tap"/> and <see cref="Wait"/> is set (<see cref="CaseLoader"/>
/// checks it).
/// </summary>
/// <param name="Say">The user types this message.</param>
/// <param name="Tap"><c>option</c>, <c>confirm</c>, <c>cancel</c>, <c>try_again</c> or <c>save_alias</c>.</param>
/// <param name="Value">The option tapped: a contact, bill or product id, an operator code, <c>store:internet</c>, or an amount (a string or a number).</param>
/// <param name="Pin"><c>correct</c> or <c>wrong</c>: a PIN is entered before Confirm.</param>
/// <param name="Twice">Confirm is tapped twice without waiting.</param>
/// <param name="Wait"><c>expire</c> or <c>expire_if_card</c>.</param>
/// <param name="Expect">What must hold right after this step.</param>
public sealed record CaseStep(
    string? Say = null,
    string? Tap = null,
    JsonElement? Value = null,
    string? Pin = null,
    bool Twice = false,
    string? Wait = null,
    StepExpect? Expect = null)
{
    /// <summary>The option value as text: the string as written, or a number's digits.</summary>
    public string? ValueText => CaseValues.Text(Value);
}

/// <summary>
/// What must hold right after a step. <c>no_card_N</c> keys (a compound request's part N gives no card) land in
/// <see cref="NoCard"/>; <c>alias_offer</c> lands in <see cref="AliasOfferSet"/> and <see cref="AliasOffer"/>, so an
/// explicit null ("no offer") is told from an absent key ("not graded"); any other unknown key is refused by
/// <see cref="CaseLoader"/>.
/// </summary>
public sealed record StepExpect(
    string? Card = null,
    string? CardOutcome = null,
    string? Reply = null,
    long? Moved = null,
    IReadOnlyList<string>? StatusSequence = null,
    int? Executions = null,
    int? PinAttemptsLeft = null,
    string? ThenReply = null,
    bool? NewRequest = null,
    bool? PinInChatOrLogs = null)
{
    /// <summary>The keys this record does not name, kept for <see cref="CaseLoader"/> to check.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>The <c>no_card_N</c> expectations, by part number.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<int, bool> NoCard { get; init; } = new Dictionary<int, bool>();

    /// <summary>True when the step names <c>alias_offer</c>, with a text or an explicit null (milestone 9a, ruling 9).</summary>
    [JsonIgnore]
    public bool AliasOfferSet { get; init; }

    /// <summary>The alias text the step expects offered; null with <see cref="AliasOfferSet"/> means no offer.</summary>
    /// <remarks>
    /// Not dead: an ignored property still claims its JSON name, so without this rename the serializer would swallow
    /// <c>alias_offer</c> and it would never reach <see cref="Extra"/>, where <see cref="CaseLoader"/> reads it. The four
    /// alias tests of <c>CaseLoaderTests</c> fail without it (checked in the milestone 9a security fix wave).
    /// </remarks>
    [JsonIgnore]
    [JsonPropertyName("loaded_alias_offer")]
    public string? AliasOffer { get; init; }
}

/// <summary>The fault of a case: <see cref="Code"/> from <c>settings.json</c>, and when it is injected.</summary>
/// <param name="Code">One of the six fault codes.</param>
/// <param name="On"><c>request</c> (the model call), <c>confirm</c> (the first Confirm) or <c>card_waiting</c>.</param>
/// <param name="Times">How many calls it hits (default one).</param>
/// <param name="Amount">The external debit's amount, in IQD.</param>
public sealed record CaseFault(string Code, string On, int? Times = null, long? Amount = null);

/// <summary>The answer key of a case.</summary>
public sealed record Expectation(
    IReadOnlyList<ExpectedAsk>? Asks = null,
    IReadOnlyList<ExpectedCard>? Cards = null,
    IReadOnlyList<ExpectedReply>? Replies = null,
    ExpectedRefusal? Refusal = null,
    bool? ConsentScreen = null,
    ExpectedFinal? Final = null,
    ExpectedLearning? LearningLog = null,
    IReadOnlyList<ExpectedAlternative>? Either = null);

/// <summary>A question expected: its kind, and either the options offered (a set) or <c>answer: typed</c>.</summary>
public sealed record ExpectedAsk(string Kind, string? Answer = null, IReadOnlyList<JsonElement>? Options = null)
{
    /// <summary>The options as text (strings as written, numbers as digits).</summary>
    public IReadOnlyList<string>? OptionTexts => Options?.Select(o => CaseValues.Text(o) ?? string.Empty).ToList();
}

/// <summary>A card expected, with every graded field; which fields apply depends on <see cref="Kind"/>.</summary>
public sealed record ExpectedCard(
    string Kind,
    string? To = null,
    string? ToNumber = null,
    string? Display = null,
    string? RegisteredName = null,
    string? Bill = null,
    string? Biller = null,
    string? Label = null,
    string? Product = null,
    long? Amount = null,
    long? Fee = null,
    long? Total = null,
    long? BalanceAfter = null,
    bool? PinRequired = null,
    bool? FirstTime = null,
    string? Warning = null,
    string? Outcome = null);

/// <summary>A reply that carries information: its reason code, its figure, and what was not prepared.</summary>
public sealed record ExpectedReply(string Reply, long? Amount = null, string? NotPrepared = null);

/// <summary>The refusal when the request gets no card, with the figures the key names.</summary>
public sealed record ExpectedRefusal(
    string Reason,
    long? Limit = null,
    long? LeftToday = null,
    string? Biller = null,
    IReadOnlyList<string>? Available = null,
    string? What = null,
    bool? NumberInReply = null,
    long? AmountDue = null);

/// <summary>The wallet after the case.</summary>
public sealed record ExpectedFinal(
    long Balance,
    IReadOnlyList<ExpectedTransaction>? Transactions = null,
    int CodesIssued = 0,
    IReadOnlyDictionary<string, long>? BillsNow = null,
    string? AliasSaved = null);

/// <summary>A new transaction expected in the wallet.</summary>
public sealed record ExpectedTransaction(
    string Type,
    long Amount,
    long Fee,
    string? ToNumber = null,
    string? To = null,
    string? Bill = null,
    string? Product = null);

/// <summary>The learning-log entry expected.</summary>
public sealed record ExpectedLearning(string Reason, string? Detail = null);

/// <summary>
/// One acceptable outcome of an <c>either</c> key (GAP-08): the cards, the reply (a reason) and the learning-log entry.
/// The dev file's alternatives have this shape, not the full <see cref="Expectation"/>.
/// </summary>
public sealed record ExpectedAlternative(
    IReadOnlyList<ExpectedCard>? Cards = null,
    ExpectedAlternativeReply? Reply = null,
    ExpectedLearning? LearningLog = null);

/// <summary>The reply of an <c>either</c> alternative.</summary>
public sealed record ExpectedAlternativeReply(string Reason);

/// <summary>Reads a value that a case file writes as a string or a number.</summary>
public static class CaseValues
{
    /// <summary>The string as written, or a number's digits (invariant); null for null or absent.</summary>
    public static string? Text(JsonElement? value) => value switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } v => v.GetString(),
        { ValueKind: JsonValueKind.Number } v => v.TryGetInt64(out var whole)
            ? whole.ToString(CultureInfo.InvariantCulture)
            : v.GetDecimal().ToString(CultureInfo.InvariantCulture),
        { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        _ => throw new JsonException("A value must be a string or a number."),
    };
}
