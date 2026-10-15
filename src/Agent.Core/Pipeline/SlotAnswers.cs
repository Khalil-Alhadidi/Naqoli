namespace Agent.Core.Pipeline;

/// <summary>
/// What the user's taps have answered for the pending request (D3): built by code from the tapped option's value,
/// never by the model, and never sent to the parse call (rule 5). The orchestrator reads a slot only to pick among
/// what it resolved itself: a contact option that is not among the resolver's candidates, or a bill or product code
/// that is not the user's bill or a catalogue product, picks nothing.
/// </summary>
/// <param name="ContactOption">
/// The value of a tapped <c>which_contact</c> or <c>did_you_mean</c> option (<see cref="Orchestrator.ContactOptionValue"/>):
/// a contact code, or <see cref="Orchestrator.AccountOptionPrefix"/> and an account ID.
/// </param>
/// <param name="BillCode">The bill code of a tapped <c>which_bill</c> or <c>bill_or_store</c> option.</param>
/// <param name="ProductCode">The product code of a tapped <c>which_product</c> option.</param>
public sealed record SlotAnswers(string? ContactOption = null, string? BillCode = null, string? ProductCode = null)
{
    /// <summary>Nothing answered by a tap.</summary>
    public static SlotAnswers None { get; } = new();
}
