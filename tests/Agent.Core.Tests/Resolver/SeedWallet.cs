using Agent.Core.Tests.Wallet;
using Agent.Core.Wallet;

namespace Agent.Core.Tests.Resolver;

/// <summary>
/// The wallet's bill and product shapes built straight from the seed files (no wallet, no database). Ids are the
/// index in the seed file plus one, as the wallet assigns them on load.
/// </summary>
internal static class SeedWallet
{
    /// <summary>The bills of <paramref name="userCode"/>, in seed order.</summary>
    public static IReadOnlyList<WalletBill> BillsOf(string userCode)
    {
        var seed = SeedFacts.Seed;
        return seed.Bills
            .Select((bill, index) => (Bill: bill, Id: index + 1))
            .Where(b => b.Bill.User == userCode)
            .Select(b =>
            {
                var biller = seed.Billers.Single(x => x.Code == b.Bill.Biller);
                return new WalletBill(
                    b.Id, b.Bill.Id, biller.Code, biller.NameAr, biller.NameEn, b.Bill.Label, b.Bill.SubscriberNumber, b.Bill.AmountDue);
            })
            .ToList();
    }

    /// <summary>The whole store catalogue, in seed order.</summary>
    public static IReadOnlyList<WalletProduct> Products()
        => SeedFacts.Seed.Products
            .Select((p, index) => new WalletProduct(
                index + 1, p.Id, p.Provider, p.ProviderNameEn, p.ProviderNameAr, p.AlsoCalled, p.Type, p.FaceValue, p.Price))
            .ToList();

    /// <summary>The seed label of bill <paramref name="billCode"/>.</summary>
    public static string LabelOf(string billCode) => SeedFacts.Seed.Bills.Single(b => b.Id == billCode).Label!;
}
