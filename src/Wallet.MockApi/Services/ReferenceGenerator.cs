using System.Globalization;

namespace Wallet.MockApi.Services;

/// <summary>
/// The customer-facing reference of a transaction: <c>ZC-</c> and the row's identity value padded to 10 digits.
/// It follows the identity, so references are the same after every reset, and match the seed's.
/// </summary>
public static class ReferenceGenerator
{
    public const string Prefix = "ZC-";

    public static string ForId(int transactionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transactionId);
        return Prefix + transactionId.ToString("D10", CultureInfo.InvariantCulture);
    }
}
