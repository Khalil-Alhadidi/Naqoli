using Agent.Core.Wallet;

namespace Agent.Core.Resolver;

/// <summary>
/// The provider names of a biller, read from the wallet's biller names (ruling 1 of milestone 8, 16 Oct 2026): the
/// first word inside the parentheses of the Arabic name, then of the English name (<c>Internet (Earthlink
/// subscription)</c> gives <c>Earthlink</c>). A name with no closed, non-empty parentheses gives none. Pure.
/// </summary>
public static class ProviderNames
{
    /// <summary>The provider names of <paramref name="bill"/>'s biller: Arabic first, then English; empty when none.</summary>
    public static IReadOnlyList<string> Of(WalletBill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);

        List<string> names = [];
        foreach (var name in (ReadOnlySpan<string>)[bill.BillerName, bill.BillerNameEn])
        {
            if (Parenthesised(name) is { } provider)
                names.Add(provider);
        }

        return names;
    }

    private static string? Parenthesised(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var open = name.IndexOf('(', StringComparison.Ordinal);
        if (open < 0)
            return null;

        var close = name.IndexOf(')', open + 1);
        if (close < 0)
            return null;

        var words = name[(open + 1)..close].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length > 0 ? words[0] : null;
    }
}
