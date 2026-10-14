using System.Security.Cryptography;

namespace Wallet.MockApi.Services;

/// <summary>Purchase codes (D11): 13 decimal digits from a cryptographic random source.</summary>
public static class VoucherCodeGenerator
{
    public const int Length = 13;

    private const string Digits = "0123456789";

    public static string Next() => RandomNumberGenerator.GetString(Digits, Length);
}
