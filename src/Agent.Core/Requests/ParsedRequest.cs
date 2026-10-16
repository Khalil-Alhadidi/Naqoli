namespace Agent.Core.Requests;

/// <summary>
/// The strict structure the parse call returns (built by tests until milestone 6). Every text is the user's own words;
/// nothing in it comes from the wallet (rule 5). Code validates it and treats it as a request to check, never as a
/// command (rule 2).
/// </summary>
public sealed record ParsedRequest
{
    public required RequestIntent Intent { get; init; }

    /// <summary>The recipient as typed: a name, an alias or a masked number token.</summary>
    public string? RecipientText { get; init; }

    /// <summary>The amount as a number; rule 19 (comma is thousands, dot never) is applied by the parser.</summary>
    public decimal? AmountValue { get; init; }

    public AmountUnit AmountUnit { get; init; }

    /// <summary><c>electricity</c>, <c>water</c>, <c>internet</c>, <c>school_fees</c> or null.</summary>
    public string? BillerCode { get; init; }

    /// <summary>
    /// For a bill, the user's words that name the biller or the service (schema version 2, 16 Oct 2026). Read only by
    /// the bill resolver, which looks in it for a provider name of the wallet's billers; never shown on the card.
    /// </summary>
    public string? BillerText { get; init; }

    /// <summary>The user's words for one of several bills of a biller (its label), or null.</summary>
    public string? BillLabelText { get; init; }

    /// <summary>The operator as typed, or null when the user did not name one.</summary>
    public string? OperatorText { get; init; }

    /// <summary><c>credit</c>, <c>internet</c> or null.</summary>
    public string? ProductType { get; init; }

    /// <summary>The face value the user named, in IQD, or null.</summary>
    public long? ProductValue { get; init; }

    /// <summary>
    /// For <see cref="RequestIntent.Unsupported"/>: <c>withdrawal</c>, <c>other_balance</c>, <c>other_currency</c>,
    /// <c>refers_back</c>, <c>same_as_last_time</c>, <c>choose_account</c>, <c>part_of_bill</c>,
    /// <c>credit_to_own_number</c>, <c>change_waiting_card</c>, <c>other_language</c>; otherwise null.
    /// </summary>
    public string? UnsupportedReason { get; init; }

    /// <summary>A currency other than dinars, as typed; set means the request is refused with <c>dinars_only</c>.</summary>
    public string? CurrencyText { get; init; }

    /// <summary>
    /// Set by <c>ParsedRequestValidator</c> when the parse call's structure broke a rule: the name of the field, with
    /// <see cref="Intent"/> <see cref="RequestIntent.Unclear"/>. The conversation logs it as <c>model_invalid_reply</c>.
    /// The model parser sets its own problems the same way (<c>json</c>, <c>truncated</c>, <c>refusal</c>); a
    /// <c>refusal</c> is logged as <c>model_refusal</c> with no detail.
    /// Null from the scripted parser and for every valid structure.
    /// </summary>
    public string? Problem { get; init; }

    /// <summary>A transfer request.</summary>
    public static ParsedRequest Transfer(string? recipientText, decimal? amountValue, AmountUnit unit)
        => new() { Intent = RequestIntent.Transfer, RecipientText = recipientText, AmountValue = amountValue, AmountUnit = unit };

    /// <summary>A bill request.</summary>
    public static ParsedRequest Bill(string? billerCode, string? label, decimal? amountValue, AmountUnit unit, string? billerText = null)
        => new()
        {
            Intent = RequestIntent.Bill,
            BillerCode = billerCode,
            BillerText = billerText,
            BillLabelText = label,
            AmountValue = amountValue,
            AmountUnit = unit,
        };

    /// <summary>A store purchase request.</summary>
    public static ParsedRequest Purchase(string? operatorText, string? productType, long? productValue)
        => new() { Intent = RequestIntent.Purchase, OperatorText = operatorText, ProductType = productType, ProductValue = productValue };

    /// <summary>The balance question.</summary>
    public static ParsedRequest Balance() => new() { Intent = RequestIntent.Balance };
}
