using Agent.Core.Text;
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

    /// <summary>The longest run of consecutive words compared with a name (a name of up to three words).</summary>
    public const int MaxWordsInAName = 3;

    /// <summary>
    /// True when a run of one to <see cref="MaxWordsInAName"/> consecutive words of <paramref name="text"/> has the
    /// <see cref="ArabicText.Key"/> of one of <paramref name="names"/>: compared as contact names are (rule 1: the
    /// normalising, a leading <c>al</c> dropped, spaces inside a name ignored), ignoring case for English. False for a
    /// null or blank text, or no names.
    /// </summary>
    public static bool AnyNamed(string? text, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var keys = new HashSet<string>(
            names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(ArabicText.Key).Where(k => k.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0)
            return false;

        var words = ArabicText.Words(text);
        for (var start = 0; start < words.Count; start++)
        {
            for (var length = 1; length <= MaxWordsInAName && start + length <= words.Count; length++)
            {
                if (keys.Contains(ArabicText.Key(string.Join(' ', words.Skip(start).Take(length)))))
                    return true;
            }
        }

        return false;
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
