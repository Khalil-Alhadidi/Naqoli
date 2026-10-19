using Agent.Core.Masking;
using Agent.Core.Requests;
using Agent.Core.Text;

namespace Agent.Core.Parsing;

/// <summary>
/// The validator's verdict: the requests to hand on, in the order spoken (D7), and the name of the field that broke a
/// rule (null when none did). With a problem, <paramref name="Requests"/> is one <see cref="RequestIntent.Unclear"/>
/// request carrying it: one broken entry refuses the whole message.
/// </summary>
public sealed record ValidationResult(IReadOnlyList<ParsedRequest> Requests, string? Problem)
{
    /// <summary>The first request (the only one for the single form, or when there is a problem).</summary>
    public ParsedRequest First => Requests[0];
}

/// <summary>
/// Checks the parse call's strict structure as untrusted output (rule 2, review I3) before code acts on it. Enum strings
/// must be in their fixed sets (exact, lower case), texts at most <see cref="MaxTextLength"/> characters, the amount
/// finite and not below zero, the product value above zero, and the recipient either exactly one token of this
/// message's <see cref="MaskMap"/> or words with no run of seven or more digits. Any violation gives
/// <see cref="RequestIntent.Unclear"/> with <see cref="ParsedRequest.Problem"/> naming the field (its wire name); it
/// never throws for bad content. A token stays a token here: the conversation un-masks it after the parse call.
/// </summary>
public static class ParsedRequestValidator
{
    /// <summary>The longest text a field may hold (the database's text columns hold as much).</summary>
    public const int MaxTextLength = 100;

    /// <summary>
    /// The eleven documented unsupported reasons (<c>docs/agent-core.md</c>, <c>ParsedRequest</c>); the model's schema
    /// takes its enum from this list. Schema version 3 (16 Oct 2026) adds <c>receive_from</c>, last.
    /// </summary>
    public static IReadOnlyList<string> UnsupportedReasons { get; } =
    [
        "withdrawal",
        "other_balance",
        "other_currency",
        "refers_back",
        "same_as_last_time",
        "choose_account",
        "part_of_bill",
        "credit_to_own_number",
        "change_waiting_card",
        "other_language",
        "receive_from",
    ];

    private static readonly Dictionary<string, RequestIntent> Intents = new(StringComparer.Ordinal)
    {
        ["transfer"] = RequestIntent.Transfer,
        ["bill"] = RequestIntent.Bill,
        ["purchase"] = RequestIntent.Purchase,
        ["balance"] = RequestIntent.Balance,
        ["unsupported"] = RequestIntent.Unsupported,
        ["unclear"] = RequestIntent.Unclear,
    };

    /// <summary>The intent names the structure may carry (rule 11: none of them returns or refunds anything).</summary>
    internal static IReadOnlyCollection<string> IntentNames => Intents.Keys;

    private static readonly Dictionary<string, AmountUnit> Units = new(StringComparer.Ordinal)
    {
        ["none"] = AmountUnit.None,
        ["dinars"] = AmountUnit.Dinars,
        ["thousand"] = AmountUnit.Thousand,
        ["million"] = AmountUnit.Million,
    };

    private static readonly HashSet<string> BillerCodes = new(StringComparer.Ordinal) { "electricity", "water", "internet", "school_fees" };

    private static readonly HashSet<string> ProductTypes = new(StringComparer.Ordinal) { "credit", "internet" };

    private static readonly HashSet<string> Reasons = new(UnsupportedReasons, StringComparer.Ordinal);

    /// <summary>
    /// Validates every request of <paramref name="reply"/> in order and stops at the first that breaks a rule (D7: the
    /// whole message is refused, as one broken request is). An empty or missing list, or a null entry, is a problem
    /// (<c>requests</c>).
    /// </summary>
    public static ValidationResult Validate(ParsedRequestsDto reply, MaskMap map)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(map);

        if (reply.Requests is not { Count: > 0 } entries)
            return Invalid("requests");

        var requests = new List<ParsedRequest>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry is null)
                return Invalid("requests");

            var result = Validate(entry, map);
            if (result.Problem is not null)
                return result;
            requests.Add(result.First);
        }

        return new ValidationResult(requests, null);
    }

    /// <summary>Validates one request against the fixed sets and limits, and its recipient against <paramref name="map"/>.</summary>
    public static ValidationResult Validate(ParsedRequestDto dto, MaskMap map)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(map);

        if (dto.Intent is null || !Intents.TryGetValue(dto.Intent, out var intent))
            return Invalid("intent");
        if (dto.AmountUnit is null || !Units.TryGetValue(dto.AmountUnit, out var unit))
            return Invalid("amount_unit");
        if (dto.BillerCode is not null && !BillerCodes.Contains(dto.BillerCode))
            return Invalid("biller_code");
        if (dto.ProductType is not null && !ProductTypes.Contains(dto.ProductType))
            return Invalid("product_type");
        if (dto.UnsupportedReason is not null && !Reasons.Contains(dto.UnsupportedReason))
            return Invalid("unsupported_reason");

        if (TooLong(dto.RecipientText))
            return Invalid("recipient_text");
        if (TooLong(dto.BillerText))
            return Invalid("biller_text");
        if (TooLong(dto.BillLabelText))
            return Invalid("bill_label_text");
        if (TooLong(dto.OperatorText))
            return Invalid("operator_text");
        if (TooLong(dto.CurrencyText))
            return Invalid("currency_text");

        decimal? amount = null;
        if (dto.AmountValue is { } value)
        {
            // The upper bound keeps the conversion to decimal from overflowing; no real amount comes near it.
            if (!double.IsFinite(value) || value < 0 || value >= 1e28)
                return Invalid("amount_value");
            amount = (decimal)value;
        }

        if (dto.ProductValue is <= 0)
            return Invalid("product_value");

        var recipient = Blank(dto.RecipientText);
        if (recipient is not null && !RecipientIsSafe(recipient, map))
            return Invalid("recipient_text");

        var request = new ParsedRequest
        {
            Intent = intent,
            RecipientText = recipient,
            AmountValue = amount,
            AmountUnit = unit,
            BillerCode = dto.BillerCode,
            BillerText = Blank(dto.BillerText),
            BillLabelText = Blank(dto.BillLabelText),
            OperatorText = Blank(dto.OperatorText),
            ProductType = dto.ProductType,
            ProductValue = dto.ProductValue,
            UnsupportedReason = dto.UnsupportedReason,
            CurrencyText = Blank(dto.CurrencyText),
        };
        return new ValidationResult([request], null);
    }

    /// <summary>
    /// Exactly one token of this message's map, or words with no number in them and no token the map does not hold (the
    /// user typed no such number: the parse call invented it). "A number" is what the masker masks or what the orchestrator
    /// would read as a phone number (<see cref="PhoneNumbers.LooksLikeNumber"/>), so the guard refuses everything the code
    /// it protects would treat as one (review I1).
    /// </summary>
    private static bool RecipientIsSafe(string recipient, MaskMap map)
        => MaskMap.IsToken(recipient)
            ? map.TryUnmask(recipient, out _)
            : !Masker.HasNumberRun(recipient) && !PhoneNumbers.LooksLikeNumber(recipient) && map.KnowsEveryToken(recipient);

    private static bool TooLong(string? text) => text is { Length: > MaxTextLength };

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static ValidationResult Invalid(string field)
        => new([new ParsedRequest { Intent = RequestIntent.Unclear, Problem = field }], field);
}
