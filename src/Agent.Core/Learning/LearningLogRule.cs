using System.Globalization;
using Agent.Core.Pipeline;
using Agent.Core.Requests;

namespace Agent.Core.Learning;

/// <summary>
/// Decides what goes to the learning log (D12, <c>data/seed/settings.json</c> <c>learning_log</c>): a pure function of
/// the reply and the parsed request. The table arrives in milestone 6, which persists exactly the entry returned here;
/// until then the runner grades it. <see cref="Logged"/> and <see cref="NotLogged"/> are the lists of
/// <c>settings.json</c>, pinned by <c>LearningLogRuleTests</c>.
/// </summary>
public static class LearningLogRule
{
    public const string NotUnderstood = ReasonCodes.NotUnderstood;
    public const string NotSupported = ReasonCodes.NotSupported;
    public const string RecipientNotFound = ReasonCodes.RecipientNotFound;
    public const string BillNotFound = ReasonCodes.BillNotFound;
    public const string BillerUnavailable = ReasonCodes.BillerUnavailable;
    public const string ProductNotFound = ReasonCodes.ProductNotFound;

    /// <summary>A message with more requests than the limit; returned by the caller that splits the message (milestone 9).</summary>
    public const string MoreThanThreeRequests = "more_than_three_requests";

    /// <summary>A model reply that failed validation; returned by the model parser (milestone 6).</summary>
    public const string ModelInvalidReply = "model_invalid_reply";

    /// <summary>
    /// A message the model refused to parse (its stop reason <c>refusal</c>); written by the conversation when the parser
    /// reports <see cref="Parsing.ClaudeParser.RefusalProblem"/>, with no detail. In <see cref="Logged"/> after
    /// <see cref="ModelInvalidReply"/>, as in the seed's <c>learning_log.logged</c> (seed-v3, 16 Oct 2026).
    /// </summary>
    public const string ModelRefusal = "model_refusal";

    public const string SameAsLastTime = "same_as_last_time";
    public const string RefersBack = "refers_back";
    public const string ChangeWaitingCard = "change_waiting_card";
    public const string ChooseAccount = "choose_account";
    public const string PartOfBill = "part_of_bill";
    public const string CreditToOwnNumber = "credit_to_own_number";
    public const string OtherCurrency = "other_currency";
    public const string OtherLanguage = "other_language";

    /// <summary>
    /// A request to receive, collect or pull money from someone else (ADD-08, ADD-09; prompt version 4, 16 Oct 2026):
    /// logged under its own name, last in <see cref="Logged"/> as in the seed's <c>learning_log.logged</c> (seed-v4).
    /// </summary>
    public const string ReceiveFrom = "receive_from";

    /// <summary>
    /// The unsupported reason for a question about someone else's balance: the reply is <c>only_own_balance</c>, and the
    /// learning row <c>not_supported</c> with this as its detail (RED-10). Not a logged name of its own.
    /// </summary>
    public const string OtherBalance = "other_balance";

    /// <summary>The reasons that are logged, in the order of <c>settings.json</c>.</summary>
    public static IReadOnlyList<string> Logged { get; } =
    [
        NotUnderstood,
        NotSupported,
        RecipientNotFound,
        BillNotFound,
        BillerUnavailable,
        ProductNotFound,
        MoreThanThreeRequests,
        ModelInvalidReply,
        ModelRefusal,
        SameAsLastTime,
        RefersBack,
        ChangeWaitingCard,
        ChooseAccount,
        PartOfBill,
        CreditToOwnNumber,
        OtherCurrency,
        OtherLanguage,
        ReceiveFrom,
    ];

    /// <summary>The refusal codes that are never logged: the agent understood, and the wallet's rules said no.</summary>
    public static IReadOnlyList<string> NotLogged { get; } =
    [
        ReasonCodes.InsufficientBalance,
        ReasonCodes.OverPerTransactionCap,
        ReasonCodes.OverDailyCap,
        ReasonCodes.NothingDue,
        ReasonCodes.AccountClosed,
        ReasonCodes.NumberHasNoWallet,
        ReasonCodes.ConsentRequired,
    ];

    /// <summary>The refusal codes that are logged under their own name.</summary>
    private static readonly HashSet<string> LoggedRefusals = new(StringComparer.Ordinal)
    {
        RecipientNotFound, BillNotFound, BillerUnavailable, ProductNotFound, NotUnderstood, NotSupported,
    };

    /// <summary>The unsupported reasons that are logged under their own name; any other is <c>not_supported</c> with the reason as detail.</summary>
    private static readonly HashSet<string> LoggedUnsupportedReasons = new(StringComparer.Ordinal)
    {
        SameAsLastTime, RefersBack, ChangeWaitingCard, ChooseAccount, PartOfBill, CreditToOwnNumber, OtherCurrency, OtherLanguage,
        ReceiveFrom,
    };

    /// <summary>
    /// The learning-log entry for <paramref name="reply"/> to <paramref name="request"/>, or null when nothing is logged.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>A <see cref="AgentReply.Refusal"/> whose code is logged gives that code, with the reply's detail; when the
    /// reply has none, a <c>bill_not_found</c> takes the biller asked for, and a <c>product_not_found</c> the operator,
    /// type and value asked for.</item>
    /// <item>A <c>dinars_only</c> refusal (the request named another currency, or was parsed as unsupported
    /// <c>other_currency</c>, GAP-07) gives <c>other_currency</c>.</item>
    /// <item>A <c>bills_paid_in_full</c> refusal with detail <c>part_of_bill</c> (ruling 2 of milestone 8) gives
    /// <c>part_of_bill</c>; without that detail (D9, a named amount that is not the amount due) it is not logged.</item>
    /// <item>A card issued with the note <c>account_choice_unavailable</c> (the known gap, GAP-04) gives
    /// <c>choose_account</c>.</item>
    /// <item>An <c>only_own_balance</c> refusal (a question about someone else's balance, RED-10) gives
    /// <c>not_supported</c> with detail <c>other_balance</c>.</item>
    /// <item>An <see cref="AgentReply.Unsupported"/> gives its reason when that is a logged name (<c>other_language</c>,
    /// a message in another language, GAP-08, among them), and otherwise <c>not_supported</c> with the reason as detail
    /// (<c>withdrawal</c>).</item>
    /// <item>Everything else, including the <see cref="NotLogged"/> codes, gives null. <c>more_than_three_requests</c>,
    /// <c>model_invalid_reply</c> and <c>model_refusal</c> are not replies; the callers that know them build their entries.</item>
    /// </list>
    /// </remarks>
    public static LearningEntry? EntryFor(AgentReply reply, ParsedRequest request)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(request);

        switch (reply)
        {
            case AgentReply.Refusal { Code: ReasonCodes.DinarsOnly }:
                return new LearningEntry(OtherCurrency, null);

            case AgentReply.CardIssued { Note: ReasonCodes.AccountChoiceUnavailable }:
                return new LearningEntry(ChooseAccount, null);

            case AgentReply.Refusal { Code: ReasonCodes.BillsPaidInFull, Detail: PartOfBill }:
                return new LearningEntry(PartOfBill, null);

            case AgentReply.Refusal { Code: ReasonCodes.OnlyOwnBalance }:
                return new LearningEntry(NotSupported, OtherBalance);

            case AgentReply.Refusal refusal when LoggedRefusals.Contains(refusal.Code):
                return new LearningEntry(refusal.Code, refusal.Detail ?? DetailFrom(refusal.Code, request));

            case AgentReply.Unsupported unsupported when LoggedUnsupportedReasons.Contains(unsupported.Reason):
                return new LearningEntry(unsupported.Reason, null);

            case AgentReply.Unsupported unsupported:
                return new LearningEntry(
                    NotSupported,
                    unsupported.Reason == NotSupported ? null : unsupported.Reason);

            default:
                return null;
        }
    }

    /// <summary>What the request asked for, as the detail of a refusal that came without one.</summary>
    private static string? DetailFrom(string code, ParsedRequest request)
    {
        switch (code)
        {
            case BillNotFound:
                return request.BillerCode;

            case ProductNotFound:
                return ProductDetail(null, request.OperatorText, request.ProductType, request.ProductValue);

            default:
                return null;
        }
    }

    /// <summary>
    /// The learning-log detail of a <c>product_not_found</c> refusal (ruling 2 of milestone 9b, NOGO-12): the provider
    /// code when the typed operator named one, else the typed operator text as the user wrote it, then the product type,
    /// then the value in dinars, space-separated, each part dropped when missing. Null when every part is missing.
    /// </summary>
    /// <param name="providerCode">The provider the typed operator named (<c>ProductResolution.NotFound.ProviderCode</c>).</param>
    /// <param name="operatorText">The operator as the user typed it; used only when <paramref name="providerCode"/> is missing.</param>
    /// <param name="productType">The product type asked for (<c>credit</c>, <c>internet</c>).</param>
    /// <param name="productValue">The face value asked for, in dinars.</param>
    public static string? ProductDetail(string? providerCode, string? operatorText, string? productType, long? productValue)
    {
        string?[] parts =
        [
            string.IsNullOrWhiteSpace(providerCode) ? operatorText : providerCode,
            productType,
            productValue?.ToString(CultureInfo.InvariantCulture),
        ];
        var named = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return named.Length == 0 ? null : string.Join(' ', named);
    }
}
