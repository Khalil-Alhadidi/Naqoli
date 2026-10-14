using System.Globalization;
using System.Security.Cryptography;

namespace Wallet.MockApi.Services;

/// <summary>
/// Hashes and checks PINs with PBKDF2 (SHA-256, 100 000 iterations, 16-byte random salt).
/// Stored form: <c>pbkdf2$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;</c>.
/// </summary>
public static class PinHasher
{
    private const string Prefix = "pbkdf2";
    private const char Separator = '$';
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MaxIterations = 10_000_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, Algorithm, HashSize);

        return string.Join(Separator,
            Prefix,
            Iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>Returns true only when <paramref name="pin"/> matches; a malformed hash never matches.</summary>
    public static bool Verify(string pin, string hash)
    {
        if (pin is null || string.IsNullOrEmpty(hash))
            return false;

        var parts = hash.Split(Separator);
        if (parts.Length != 4 || parts[0] != Prefix)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1 || iterations > MaxIterations)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
