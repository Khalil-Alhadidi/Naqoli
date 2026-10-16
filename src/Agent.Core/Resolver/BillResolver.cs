using Agent.Core.Requests;
using Agent.Core.Text;
using Agent.Core.Wallet;

namespace Agent.Core.Resolver;

/// <summary>The outcome of resolving a bill request against the user's bills.</summary>
public abstract record BillResolution
{
    private BillResolution()
    {
    }

    /// <summary>One bill with an amount due: it goes to the card.</summary>
    public sealed record Card(WalletBill Bill) : BillResolution;

    /// <summary>A <c>which_bill</c> question with these bills as options, in wallet order.</summary>
    public sealed record Options(IReadOnlyList<WalletBill> Bills) : BillResolution;

    /// <summary>
    /// Rule 13: "pay the internet" from a user with an internet bill that has an amount due. A <c>bill_or_store</c>
    /// question: the bill, or a card from the store (<see cref="InternetCards"/>, for the next question).
    /// </summary>
    public sealed record AskBillOrStore(WalletBill Bill, IReadOnlyList<WalletProduct> InternetCards) : BillResolution;

    /// <summary>Rule 17: "pay the internet" from a user with no internet bill. A <c>which_product</c> question.</summary>
    public sealed record StoreInternet(IReadOnlyList<WalletProduct> InternetCards) : BillResolution;

    /// <summary>
    /// The bill is found but nothing is due (D9): refusal <c>nothing_due</c>. With no biller named, the user has bills but
    /// none is due, and <see cref="Bill"/> is the first of them in wallet order.
    /// </summary>
    public sealed record NothingDue(WalletBill Bill) : BillResolution;

    /// <summary>No bill fits: refusal <c>bill_not_found</c>.</summary>
    public sealed record NotFound : BillResolution;
}

/// <summary>Resolves a bill request by matching rules 13, 14 and 17 and D9. Pure: no I/O, no wallet calls.</summary>
public static class BillResolver
{
    /// <summary>The biller code of an internet subscription.</summary>
    public const string InternetBiller = "internet";

    /// <summary>The store product type of an internet card.</summary>
    public const string InternetProductType = "internet";

    /// <param name="request">
    /// The parsed request; <see cref="ParsedRequest.BillerCode"/>, <see cref="ParsedRequest.BillerText"/> and
    /// <see cref="ParsedRequest.BillLabelText"/> are read.
    /// </param>
    /// <param name="bills">The user's bills, in wallet order.</param>
    /// <param name="products">The store catalogue, in wallet order (for the internet cards of rules 13 and 17).</param>
    /// <param name="options">The agent's options; no limit applies to bills today.</param>
    public static BillResolution Resolve(
        ParsedRequest request, IReadOnlyList<WalletBill> bills, IReadOnlyList<WalletProduct> products, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bills);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(options);

        // Rule 14: no biller named lists every bill that has an amount due; one such bill goes to the card. With bills
        // but none due, nothing is due (Khalil, 14 Oct 2026); with no bills at all, no bill is found.
        if (string.IsNullOrWhiteSpace(request.BillerCode))
        {
            var due = bills.Where(b => b.AmountDueIqd > 0).ToList();
            return due.Count switch
            {
                0 when bills.Count > 0 => new BillResolution.NothingDue(bills[0]),
                0 => new BillResolution.NotFound(),
                1 => new BillResolution.Card(due[0]),
                _ => new BillResolution.Options(due),
            };
        }

        var isInternet = string.Equals(request.BillerCode, InternetBiller, StringComparison.Ordinal);
        var ofBiller = bills.Where(b => string.Equals(b.BillerCode, request.BillerCode, StringComparison.Ordinal)).ToList();

        if (ofBiller.Count == 0)
        {
            // Rule 17: no internet bill means the store's internet cards.
            return isInternet ? new BillResolution.StoreInternet(InternetCards(products)) : new BillResolution.NotFound();
        }

        if (ofBiller.Count == 1)
        {
            var bill = ofBiller[0];
            if (bill.AmountDueIqd <= 0)
                return new BillResolution.NothingDue(bill);

            // Rule 13: the internet bill, or a card from the store; a provider name of the bill in the user's words
            // means the bill (the bill-or-store rule, Khalil, 16 Oct 2026).
            return isInternet && !NamesProvider(request.BillerText, bill)
                ? new BillResolution.AskBillOrStore(bill, InternetCards(products))
                : new BillResolution.Card(bill);
        }

        // Several bills of one biller: the label the user named picks one, else ask (all of them, due or not).
        if (!string.IsNullOrWhiteSpace(request.BillLabelText))
        {
            var labelKey = ArabicText.Key(request.BillLabelText);
            var labelled = ofBiller
                .Where(b => b.Label is not null && labelKey.Length > 0 && ArabicText.Key(b.Label) == labelKey)
                .ToList();
            if (labelled.Count == 1)
            {
                return labelled[0].AmountDueIqd > 0
                    ? new BillResolution.Card(labelled[0])
                    : new BillResolution.NothingDue(labelled[0]);
            }
        }

        return new BillResolution.Options(ofBiller);
    }

    /// <summary>
    /// True when a word of <paramref name="billerText"/> is a provider name of <paramref name="bill"/>
    /// (<see cref="ProviderNames.Of"/>): equal after <see cref="ArabicText.Key"/> (rule 1: a leading <c>al</c> dropped),
    /// ignoring case for the English name. False for a null or blank text.
    /// </summary>
    public static bool NamesProvider(string? billerText, WalletBill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        if (string.IsNullOrWhiteSpace(billerText))
            return false;

        var names = ProviderNames.Of(bill).Select(ArabicText.Key).Where(k => k.Length > 0).ToList();
        return names.Count > 0
            && ArabicText.Words(billerText).Any(word => names.Contains(ArabicText.Key(word), StringComparer.OrdinalIgnoreCase));
    }

    private static List<WalletProduct> InternetCards(IReadOnlyList<WalletProduct> products)
        => products.Where(p => string.Equals(p.Type, InternetProductType, StringComparison.OrdinalIgnoreCase)).ToList();
}
