using Agent.Core.Pipeline;
using Wallet.MockApi.Data.Entities;
using Wallet.MockApi.Services;

namespace Agent.Core.Tests.Pipeline;

/// <summary>
/// Security review M3 of milestone 8: the daily cap counts the wallet's payment types only. Every type the wallet can
/// write must be classified here, a payment (counted) or not (an external debit), so a new type fails this test until
/// someone decides whether the cap counts it.
/// </summary>
public class PaymentTypesTests
{
    /// <summary>The wallet's types that are not payments and never count toward the daily cap.</summary>
    private static readonly HashSet<string> NotPayments = new(StringComparer.OrdinalIgnoreCase) { "externalDebit" };

    [Fact]
    public void EveryWalletTransactionTypeIsClassifiedOnce()
    {
        foreach (var type in Enum.GetValues<TransactionType>())
        {
            var name = TransactionNames.Of(type);
            var payment = Checks.PaymentTypes.Contains(name);
            Assert.True(payment ^ NotPayments.Contains(name), name + " must be either a payment or a non-payment, exactly one.");
        }
    }

    [Fact]
    public void ThePaymentTypesAreTheThreeTheWalletExecutes()
    {
        foreach (var name in new[] { "transfer", "bill", "purchase" })
        {
            Assert.Contains(name, Checks.PaymentTypes);
            Assert.True(TransactionNames.TryParse(name, out _), name);
        }

        Assert.Equal(3, Checks.PaymentTypes.Count);
    }
}
