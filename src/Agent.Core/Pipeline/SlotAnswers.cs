namespace Agent.Core.Pipeline;

/// <summary>
/// What the user's taps have answered for the pending request (D3): built by code from the tapped option's value,
/// never by the model, and never sent to the parse call (rule 5). The orchestrator reads a slot only to pick among
/// what it resolved itself: a contact option that is not among the resolver's candidates, or a bill or product code
/// that is not the user's bill or a catalogue product, picks nothing.
/// </summary>
/// <param name="ContactOption">
/// The value of a tapped <c>which_contact</c>, <c>did_you_mean</c> or person <c>person_or_bill</c> option (<see cref="Orchestrator.ContactOptionValue"/>):
/// a contact code, or <see cref="Orchestrator.AccountOptionPrefix"/> and an account ID.
/// </param>
/// <param name="BillCode">The bill code of a tapped <c>which_bill</c>, <c>bill_or_store</c> or bill <c>person_or_bill</c> option.</param>
/// <param name="ProductCode">The product code of a tapped <c>which_product</c> option.</param>
/// <param name="PersonOrBill">
/// True once the user has tapped an option of a <c>person_or_bill</c> question (Khalil, 20 Oct 2026): the side is then
/// in the request's intent and in <paramref name="ContactOption"/> or <paramref name="BillCode"/>, and the question is
/// not asked again for this request.
/// </param>
public sealed record SlotAnswers(
    string? ContactOption = null, string? BillCode = null, string? ProductCode = null, bool PersonOrBill = false)
{
    /// <summary>Nothing answered by a tap.</summary>
    public static SlotAnswers None { get; } = new();
}
