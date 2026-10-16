using System.Text.Json.Serialization;

namespace Agent.Core.Parsing;

/// <summary>
/// One request as the parse call returns it: the strict structure (rule 2), snake_case on the wire. Every value is
/// untrusted until <see cref="ParsedRequestValidator"/> has checked it; a field the structure does not name is refused
/// when the reply is read. The enum strings and their sets are those of <see cref="ParsedRequestValidator"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParsedRequestDto
{
    /// <summary><c>transfer</c>, <c>bill</c>, <c>purchase</c>, <c>balance</c>, <c>unsupported</c> or <c>unclear</c>.</summary>
    [JsonPropertyName("intent")]
    public string? Intent { get; init; }

    /// <summary>The recipient as typed: a name, an alias, or a token of the masked message.</summary>
    [JsonPropertyName("recipient_text")]
    public string? RecipientText { get; init; }

    [JsonPropertyName("amount_value")]
    public double? AmountValue { get; init; }

    /// <summary><c>none</c>, <c>dinars</c>, <c>thousand</c> or <c>million</c>.</summary>
    [JsonPropertyName("amount_unit")]
    public string? AmountUnit { get; init; }

    /// <summary><c>electricity</c>, <c>water</c>, <c>internet</c>, <c>school_fees</c> or null.</summary>
    [JsonPropertyName("biller_code")]
    public string? BillerCode { get; init; }

    /// <summary>
    /// For a bill, the user's words that name the biller or the service, verbatim (schema version 2, 16 Oct 2026); read
    /// only by the bill resolver to find a provider name.
    /// </summary>
    [JsonPropertyName("biller_text")]
    public string? BillerText { get; init; }

    [JsonPropertyName("bill_label_text")]
    public string? BillLabelText { get; init; }

    [JsonPropertyName("operator_text")]
    public string? OperatorText { get; init; }

    /// <summary><c>credit</c>, <c>internet</c> or null.</summary>
    [JsonPropertyName("product_type")]
    public string? ProductType { get; init; }

    [JsonPropertyName("product_value")]
    public long? ProductValue { get; init; }

    /// <summary>One of the ten documented reasons (<see cref="ParsedRequestValidator.UnsupportedReasons"/>) or null.</summary>
    [JsonPropertyName("unsupported_reason")]
    public string? UnsupportedReason { get; init; }

    [JsonPropertyName("currency_text")]
    public string? CurrencyText { get; init; }
}

/// <summary>
/// The whole reply of the parse call: a list of requests in the order spoken (D7). Until compound requests are built
/// (milestone 9), the first is used and the others are only noted (<see cref="ValidationResult.MoreThanOneRequest"/>).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParsedRequestsDto([property: JsonPropertyName("requests")] IReadOnlyList<ParsedRequestDto>? Requests);
