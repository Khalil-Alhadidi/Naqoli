using System.Security.Cryptography;
using System.Text;

namespace Wallet.MockApi.Data.Seed;

/// <summary>
/// The hash of the seed files. Any change to a file changes it and fails the seed-hash test on purpose
/// (see <c>data/seed/README.md</c>).
/// </summary>
public static class SeedHash
{
    /// <summary>
    /// SHA-256, lower-case hex, over each file in <see cref="SeedFiles.All"/> order: the file name, a <c>\n</c>,
    /// the file's bytes with every <c>\r</c> removed (so CRLF and LF checkouts hash alike), then a <c>\n</c>.
    /// </summary>
    /// <exception cref="FileNotFoundException">A seed file is missing.</exception>
    public static string Compute(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ReadOnlySpan<byte> newline = "\n"u8;

        foreach (var file in SeedFiles.All)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Seed file {file} not found in {directory}.", path);

            var bytes = File.ReadAllBytes(path);
            var withoutCr = Array.FindAll(bytes, b => b != (byte)'\r');

            hash.AppendData(Encoding.UTF8.GetBytes(file));
            hash.AppendData(newline);
            hash.AppendData(withoutCr);
            hash.AppendData(newline);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
