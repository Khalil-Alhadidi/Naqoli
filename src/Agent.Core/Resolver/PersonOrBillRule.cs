using Agent.Core.Requests;
using Agent.Core.Text;
using Agent.Core.Wallet;

namespace Agent.Core.Resolver;

/// <summary>
/// A name that is both a person and a bill: the accounts the typed name matches exactly (one per account) and the bills
/// whose label it is, in wallet order. Both lists hold at least one entry.
/// </summary>
/// <param name="People">One candidate per account, as <see cref="ContactResolver.ExactMatches"/> gives them.</param>
/// <param name="Bills">The user's bills whose label is the typed name.</param>
public sealed record PersonOrBillMatch(IReadOnlyList<ContactCandidate> People, IReadOnlyList<WalletBill> Bills);

/// <summary>
/// The person-or-bill rule (Khalil, 20 Oct 2026, option 1 (a)): when the name the user typed is exactly both a contact
/// (a saved name or an alias, by the contact resolver's exact step, after its endearment handling) and the label of one
/// of their bills, the agent asks <c>person_or_bill</c> whatever intent the model read. Exact only: a near spelling, a
/// partial or a shared word is not the same name. Pure: no I/O, no wallet calls.
/// </summary>
public static class PersonOrBillRule
{
    /// <summary>
    /// The name the rule reads from a request: for a transfer, its recipient unless it is a number (a phone is never a
    /// name); for a bill, its label, else its biller as the user named it. Null for any other intent, or when that text
    /// is missing or blank.
    /// </summary>
    public static string? TypedText(ParsedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Intent switch
        {
            RequestIntent.Transfer when !PhoneNumbers.LooksLikeNumber(request.RecipientText) => NonBlank(request.RecipientText),
            RequestIntent.Bill => BillText(request),
            _ => null,
        };
    }

    /// <summary>The name a bill request carries: its label, else its biller as the user named it; null when neither is set.</summary>
    public static string? BillText(ParsedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return NonBlank(request.BillLabelText) ?? NonBlank(request.BillerText);
    }

    /// <summary>The people the typed name is exactly (<see cref="ContactResolver.ExactMatches"/>), one per account.</summary>
    public static IReadOnlyList<ContactCandidate> People(string typed, IReadOnlyList<ContactCandidate> candidates, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ContactResolver.ExactMatches(typed, candidates, options.Endearments);
    }

    /// <summary>
    /// The bills whose label is the typed name, compared as contact names are (<see cref="ArabicText.Key"/>), in wallet
    /// order. A bill with no label is never one.
    /// </summary>
    public static IReadOnlyList<WalletBill> Bills(string typed, IReadOnlyList<WalletBill> bills)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(bills);

        var key = ArabicText.Key(typed);
        return key.Length == 0
            ? []
            : bills.Where(b => b.Label is not null && ArabicText.Key(b.Label) == key).ToList();
    }

    /// <summary>
    /// The match when <paramref name="typed"/> is exactly both one or more of the user's people and one or more of their
    /// bill labels; null otherwise.
    /// </summary>
    /// <param name="typed">The name as the user typed it (<see cref="TypedText"/>).</param>
    /// <param name="candidates">The user's saved names and aliases (<see cref="ContactCandidate.From"/>).</param>
    /// <param name="bills">The user's bills, in wallet order.</param>
    /// <param name="options">The agent's options; <see cref="AgentOptions.Endearments"/> is read.</param>
    public static PersonOrBillMatch? Find(
        string typed, IReadOnlyList<ContactCandidate> candidates, IReadOnlyList<WalletBill> bills, AgentOptions options)
    {
        var labelled = Bills(typed, bills);
        if (labelled.Count == 0)
            return null;

        var people = People(typed, candidates, options);
        return people.Count == 0 ? null : new PersonOrBillMatch(people, labelled);
    }

    private static string? NonBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
