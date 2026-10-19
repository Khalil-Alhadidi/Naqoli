using System.Text.Json;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Agent.Core.Text;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The strict structure of the parse call is untrusted (rule 2, review I3): every enum string must be in its fixed set,
/// every text at most 100 characters, every figure in range, and a recipient either one token of this message's map or
/// words with no number in them. Any violation is <see cref="RequestIntent.Unclear"/> with the field named, never an
/// exception.
/// </summary>
public class ParsedRequestValidatorTests
{
    private const string Phone = "07701234567";

    private static readonly MaskMap Map = Masker.Mask("send to " + Phone).Map;

    private static ParsedRequestDto Transfer(string? recipient = "Ali", double? amount = 50, string? unit = "thousand")
        => new() { Intent = "transfer", RecipientText = recipient, AmountValue = amount, AmountUnit = unit };

    private static void AssertProblem(ValidationResult result, string field)
    {
        Assert.Equal(field, result.Problem);
        Assert.Equal(RequestIntent.Unclear, result.First.Intent);
        Assert.Equal(field, result.First.Problem);
        Assert.Null(result.First.RecipientText);
        Assert.Null(result.First.AmountValue);
    }

    [Fact]
    public void AValidTransferMapsEveryField()
    {
        var result = ParsedRequestValidator.Validate(Transfer(), Map);

        Assert.Null(result.Problem);
        Assert.Equal(
            new ParsedRequest { Intent = RequestIntent.Transfer, RecipientText = "Ali", AmountValue = 50m, AmountUnit = AmountUnit.Thousand },
            result.First);
    }

    [Fact]
    public void AValidBillPurchaseAndUnsupportedMapEveryField()
    {
        var bill = ParsedRequestValidator.Validate(
            new ParsedRequestDto { Intent = "bill", AmountUnit = "none", BillerCode = "water", BillerText = " water company ", BillLabelText = "home" }, Map);
        var purchase = ParsedRequestValidator.Validate(
            new ParsedRequestDto { Intent = "purchase", AmountUnit = "none", OperatorText = "zain", ProductType = "credit", ProductValue = 5_000 }, Map);
        var unsupported = ParsedRequestValidator.Validate(
            new ParsedRequestDto { Intent = "unsupported", AmountUnit = "none", UnsupportedReason = "withdrawal" }, Map);
        var currency = ParsedRequestValidator.Validate(
            new ParsedRequestDto { Intent = "transfer", RecipientText = "Ali", AmountValue = 100, AmountUnit = "none", CurrencyText = "dollars" }, Map);

        Assert.Equal(ParsedRequest.Bill("water", "home", null, AmountUnit.None, billerText: "water company"), bill.First);
        Assert.Equal(ParsedRequest.Purchase("zain", "credit", 5_000), purchase.First);
        Assert.Equal(new ParsedRequest { Intent = RequestIntent.Unsupported, UnsupportedReason = "withdrawal" }, unsupported.First);
        Assert.Equal("dollars", currency.First.CurrencyText);
        Assert.All([bill, purchase, unsupported, currency], r => Assert.Null(r.Problem));
    }

    [Theory]
    [InlineData("transfer", RequestIntent.Transfer)]
    [InlineData("bill", RequestIntent.Bill)]
    [InlineData("purchase", RequestIntent.Purchase)]
    [InlineData("balance", RequestIntent.Balance)]
    [InlineData("unsupported", RequestIntent.Unsupported)]
    [InlineData("unclear", RequestIntent.Unclear)]
    public void EveryIntentInTheSetIsRead(string intent, RequestIntent expected)
    {
        var result = ParsedRequestValidator.Validate(new ParsedRequestDto { Intent = intent, AmountUnit = "none" }, Map);

        Assert.Null(result.Problem);
        Assert.Equal(expected, result.First.Intent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Transfer")]
    [InlineData("send")]
    [InlineData("withdraw")]
    public void AnIntentOutsideTheSetIsAProblem(string? intent)
    {
        AssertProblem(ParsedRequestValidator.Validate(Transfer() with { Intent = intent }, Map), "intent");
    }

    [Theory]
    [InlineData("none", AmountUnit.None)]
    [InlineData("dinars", AmountUnit.Dinars)]
    [InlineData("thousand", AmountUnit.Thousand)]
    [InlineData("million", AmountUnit.Million)]
    public void EveryAmountUnitInTheSetIsRead(string unit, AmountUnit expected)
    {
        var result = ParsedRequestValidator.Validate(Transfer(unit: unit), Map);

        Assert.Null(result.Problem);
        Assert.Equal(expected, result.First.AmountUnit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("lakh")]
    [InlineData("Thousand")]
    public void AnAmountUnitOutsideTheSetIsAProblem(string? unit)
    {
        AssertProblem(ParsedRequestValidator.Validate(Transfer(unit: unit), Map), "amount_unit");
    }

    [Theory]
    [InlineData("electricity")]
    [InlineData("water")]
    [InlineData("internet")]
    [InlineData("school_fees")]
    [InlineData(null)]
    public void EveryBillerCodeInTheSetIsRead(string? biller)
    {
        var result = ParsedRequestValidator.Validate(new ParsedRequestDto { Intent = "bill", AmountUnit = "none", BillerCode = biller }, Map);

        Assert.Null(result.Problem);
        Assert.Equal(biller, result.First.BillerCode);
    }

    [Theory]
    [InlineData("gas")]
    [InlineData("Water")]
    [InlineData("")]
    public void ABillerCodeOutsideTheSetIsAProblem(string biller)
    {
        AssertProblem(
            ParsedRequestValidator.Validate(new ParsedRequestDto { Intent = "bill", AmountUnit = "none", BillerCode = biller }, Map),
            "biller_code");
    }

    [Theory]
    [InlineData("credit")]
    [InlineData("internet")]
    [InlineData(null)]
    public void EveryProductTypeInTheSetIsRead(string? type)
    {
        var result = ParsedRequestValidator.Validate(new ParsedRequestDto { Intent = "purchase", AmountUnit = "none", ProductType = type }, Map);

        Assert.Null(result.Problem);
        Assert.Equal(type, result.First.ProductType);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("Credit")]
    public void AProductTypeOutsideTheSetIsAProblem(string type)
    {
        AssertProblem(
            ParsedRequestValidator.Validate(new ParsedRequestDto { Intent = "purchase", AmountUnit = "none", ProductType = type }, Map),
            "product_type");
    }

    [Theory]
    [InlineData("withdrawal")]
    [InlineData("other_balance")]
    [InlineData("other_currency")]
    [InlineData("refers_back")]
    [InlineData("same_as_last_time")]
    [InlineData("choose_account")]
    [InlineData("part_of_bill")]
    [InlineData("credit_to_own_number")]
    [InlineData("change_waiting_card")]
    [InlineData("other_language")]
    [InlineData("receive_from")]
    public void EveryDocumentedUnsupportedReasonIsRead(string reason)
    {
        var result = ParsedRequestValidator.Validate(
            new ParsedRequestDto { Intent = "unsupported", AmountUnit = "none", UnsupportedReason = reason }, Map);

        Assert.Null(result.Problem);
        Assert.Equal(reason, result.First.UnsupportedReason);
    }

    [Theory]
    [InlineData("loan")]
    [InlineData("not_supported")]
    [InlineData("Withdrawal")]
    public void AnUnsupportedReasonOutsideTheSetIsAProblem(string reason)
    {
        AssertProblem(
            ParsedRequestValidator.Validate(new ParsedRequestDto { Intent = "unsupported", AmountUnit = "none", UnsupportedReason = reason }, Map),
            "unsupported_reason");
    }

    [Theory]
    [InlineData("recipient_text")]
    [InlineData("biller_text")]
    [InlineData("bill_label_text")]
    [InlineData("operator_text")]
    [InlineData("currency_text")]
    public void ATextLongerThanOneHundredCharactersIsAProblem(string field)
    {
        var limit = new string('a', ParsedRequestValidator.MaxTextLength);
        var tooLong = limit + "a";
        ParsedRequestDto With(string text) => field switch
        {
            "recipient_text" => Transfer(recipient: text),
            "biller_text" => Transfer() with { BillerText = text },
            "bill_label_text" => Transfer() with { BillLabelText = text },
            "operator_text" => Transfer() with { OperatorText = text },
            _ => Transfer() with { CurrencyText = text },
        };

        Assert.Null(ParsedRequestValidator.Validate(With(limit), Map).Problem);
        AssertProblem(ParsedRequestValidator.Validate(With(tooLong), Map), field);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e30)]
    public void AnAmountBelowZeroOrNotFiniteIsAProblem(double amount)
    {
        AssertProblem(ParsedRequestValidator.Validate(Transfer(amount: amount), Map), "amount_value");
    }

    [Fact]
    public void ZeroAndNoAmountAreRead()
    {
        Assert.Equal(0m, ParsedRequestValidator.Validate(Transfer(amount: 0), Map).First.AmountValue);
        Assert.Null(ParsedRequestValidator.Validate(Transfer(amount: null), Map).First.AmountValue);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5_000L)]
    public void AProductValueOfZeroOrLessIsAProblem(long value)
    {
        AssertProblem(
            ParsedRequestValidator.Validate(new ParsedRequestDto { Intent = "purchase", AmountUnit = "none", ProductValue = value }, Map),
            "product_value");
    }

    [Theory]
    [InlineData(Phone)]
    [InlineData("0770 123 4567")]
    [InlineData("+964 770 123 4567")]
    [InlineData("Ali 07701234567")]
    public void APhoneNumberInTheRecipientIsAProblem(string recipient)
    {
        AssertProblem(ParsedRequestValidator.Validate(Transfer(recipient: recipient), Map), "recipient_text");
    }

    [Theory]
    [InlineData(0x00A0)]
    [InlineData(0x0009)]
    [InlineData(0x000A)]
    [InlineData(0x2009)]
    [InlineData(0x200E)]
    [InlineData(0x200F)]
    [InlineData(0x061C)]
    public void APhoneNumberWithAnySeparatorInTheRecipientIsAProblem(int separator)
    {
        // Review I1: the guard refuses what the orchestrator would read as a phone number.
        AssertProblem(ParsedRequestValidator.Validate(Transfer(recipient: global::Agent.Core.Tests.Masking.MaskerTests.Split(separator)), Map), "recipient_text");
    }

    [Theory]
    [InlineData("(0770) (123) (4567)")]
    [InlineData("+964 (770) 123-4567")]
    public void APhoneNumberInBracketsInTheRecipientIsAProblem(string recipient)
    {
        AssertProblem(ParsedRequestValidator.Validate(Transfer(recipient: recipient), Map), "recipient_text");
    }

    [Fact]
    public void AnArabicIndicPhoneNumberInTheRecipientIsAProblem()
    {
        AssertProblem(ParsedRequestValidator.Validate(Transfer(recipient: ArabicDigits.ToArabicIndic(Phone)), Map), "recipient_text");
    }

    [Fact]
    public void ATokenOfThisMessageInTheRecipientPassesAndStaysAToken()
    {
        // The conversation un-masks it after the parse call; the validator only checks it.
        var result = ParsedRequestValidator.Validate(Transfer(recipient: "<NUM_1>"), Map);

        Assert.Null(result.Problem);
        Assert.Equal("<NUM_1>", result.First.RecipientText);
    }

    [Theory]
    [InlineData("<NUM_2>")]
    [InlineData("Ali <NUM_7>")]
    public void ATokenNotInThisMessagesMapIsAProblem(string recipient)
    {
        // The user typed no such number: the model invented one.
        AssertProblem(ParsedRequestValidator.Validate(Transfer(recipient: recipient), Map), "recipient_text");
        AssertProblem(ParsedRequestValidator.Validate(Transfer(recipient: "<NUM_1>"), MaskMap.Empty), "recipient_text");
    }

    [Fact]
    public void BlankTextsAreRead()
    {
        var result = ParsedRequestValidator.Validate(Transfer(recipient: "  ") with { OperatorText = string.Empty, BillerText = " " }, Map);

        Assert.Null(result.Problem);
        Assert.Null(result.First.RecipientText);
        Assert.Null(result.First.BillerText);
        Assert.Null(result.First.OperatorText);
    }

    [Fact]
    public void AnExtraFieldIsRefusedWhenTheReplyIsRead()
    {
        const string withExtra = """{"intent":"transfer","recipient_text":"Ali","amount_value":50,"amount_unit":"thousand","biller_code":null,"biller_text":null,"bill_label_text":null,"operator_text":null,"product_type":null,"product_value":null,"unsupported_reason":null,"currency_text":null,"execute":true}""";
        const string exact = """{"intent":"transfer","recipient_text":"Ali","amount_value":50,"amount_unit":"thousand","biller_code":null,"biller_text":null,"bill_label_text":null,"operator_text":null,"product_type":null,"product_value":null,"unsupported_reason":null,"currency_text":null}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParsedRequestDto>(withExtra));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParsedRequestsDto>("""{"requests":[],"note":"x"}"""));
        Assert.Equal(Transfer(), JsonSerializer.Deserialize<ParsedRequestDto>(exact));
    }

    [Fact]
    public void AListKeepsEveryRequestInOrder()
    {
        var one = ParsedRequestValidator.Validate(new ParsedRequestsDto([Transfer()]), Map);
        var two = ParsedRequestValidator.Validate(new ParsedRequestsDto([Transfer(), Transfer(recipient: "Omar")]), Map);

        Assert.Null(one.Problem);
        Assert.Equal(["Ali"], one.Requests.Select(r => r.RecipientText));
        Assert.Null(two.Problem);
        Assert.Equal(["Ali", "Omar"], two.Requests.Select(r => r.RecipientText));
    }

    [Fact]
    public void AListWithABrokenSecondEntryIsOneUnclearRequest()
    {
        // D7: a structure that breaks a rule anywhere in the list refuses the whole message, as one broken request does.
        var result = ParsedRequestValidator.Validate(new ParsedRequestsDto([Transfer(), Transfer() with { Intent = "refund" }]), Map);

        AssertProblem(result, "intent");
        Assert.Single(result.Requests);
    }

    [Fact]
    public void AListWithANullEntryIsTheProblemRequests()
    {
        var result = ParsedRequestValidator.Validate(new ParsedRequestsDto([Transfer(), null!]), Map);

        AssertProblem(result, "requests");
        Assert.Single(result.Requests);
    }

    [Fact]
    public void AnEmptyOrMissingListIsAProblem()
    {
        AssertProblem(ParsedRequestValidator.Validate(new ParsedRequestsDto([]), Map), "requests");
        AssertProblem(ParsedRequestValidator.Validate(new ParsedRequestsDto(null), Map), "requests");
    }

    private static readonly string[] ReturnWords = ["return", "refund", "reverse"];

    [Fact]
    public void TheIntentAndUnsupportedSetsCarryNoReturnOrRefundValue()
    {
        // Rule 11: a code cannot be returned, so neither the structure nor the schema the model is held to can say it.
        string[] values = [.. ParsedRequestValidator.IntentNames, .. ParsedRequestValidator.UnsupportedReasons];
        Assert.NotEmpty(ParsedRequestValidator.IntentNames);
        Assert.All(values, value => Assert.DoesNotContain(ReturnWords, word => value.Contains(word, StringComparison.OrdinalIgnoreCase)));

        var schema = ClaudeParser.Schema.GetRawText();
        Assert.All(ReturnWords, word => Assert.DoesNotContain(word, schema, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AReturnIntentOrARefundReasonIsInvalid()
    {
        AssertProblem(
            ParsedRequestValidator.Validate(new ParsedRequestDto { Intent = "return", AmountUnit = "none" }, Map),
            "intent");
        AssertProblem(
            ParsedRequestValidator.Validate(new ParsedRequestDto { Intent = "unsupported", AmountUnit = "none", UnsupportedReason = "refund" }, Map),
            "unsupported_reason");
    }
}
