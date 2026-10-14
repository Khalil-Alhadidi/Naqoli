using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wallet.MockApi.Contracts;

namespace Wallet.MockApi.Services;

/// <summary>
/// What a payment request asked for, as one value to compare: SHA-256, lower-case hex (64 characters), of
/// <c>userId|type|amountIqd|toPhone|toAccountId|billAccountId|productId</c>, each field as received (before
/// validation) and empty when absent. The PIN is not part of it, so a retry with the PIN added or corrected is still
/// the same request.
/// </summary>
public static class RequestFingerprint
{
    /// <summary>The length of every fingerprint.</summary>
    public const int Length = 64;

    public static string Of(PaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{request.UserId}|{request.Type}|{request.AmountIqd}|{request.ToPhone}|{request.ToAccountId}|{request.BillAccountId}|{request.ProductId}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
