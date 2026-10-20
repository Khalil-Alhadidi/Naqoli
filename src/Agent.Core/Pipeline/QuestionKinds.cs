using Agent.Core.Resolver;

namespace Agent.Core.Pipeline;

/// <summary>The kinds of question the agent asks, with Khalil's names (the <c>kind</c> of an answer key's <c>asks</c>).</summary>
public static class QuestionKinds
{
    public const string WhichContact = "which_contact";
    public const string DidYouMean = "did_you_mean";
    public const string WhichProduct = "which_product";
    public const string WhichBill = "which_bill";
    public const string WhichAmount = "which_amount";
    public const string BillOrStore = "bill_or_store";
    public const string WhichOperator = "which_operator";

    /// <summary>
    /// The name the user typed is exactly both a contact (a saved name or an alias) and the label of one of their bills
    /// (Khalil, 20 Oct 2026): the agent never chooses by the intent the model read. Its options are the person (one per
    /// account) and the bill (one per matching bill), each tagged with its <see cref="OptionSide"/>.
    /// </summary>
    public const string PersonOrBill = "person_or_bill";

    /// <summary>The name of a contact resolver question kind.</summary>
    public static string Of(QuestionKind kind) => kind switch
    {
        QuestionKind.WhichContact => WhichContact,
        QuestionKind.DidYouMean => DidYouMean,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown question kind."),
    };

    /// <summary>
    /// Rule 10: a missing amount or a missing recipient (<see cref="WhichAmount"/> or <see cref="WhichContact"/> with no
    /// options) is typed; every other question is answered by tapping an option.
    /// </summary>
    public static QuestionAnswer AnswerFor(string kind, int optionCount)
        => optionCount == 0 && kind is WhichAmount or WhichContact ? QuestionAnswer.Typed : QuestionAnswer.Tap;
}
