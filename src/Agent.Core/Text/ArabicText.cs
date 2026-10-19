using System.Globalization;
using System.Text;

namespace Agent.Core.Text;

/// <summary>
/// Matching rule 1: how a typed name and a saved name or alias are normalised before they are compared. Pure; the word
/// lists come from <see cref="ArabicWords"/>.
/// </summary>
public static class ArabicText
{
    private const char Tatweel = '\u0640';          // ARABIC TATWEEL
    private const char FathatanFirst = '\u064B';    // ARABIC FATHATAN (first of the harakat removed)
    private const char SukunLast = '\u0652';        // ARABIC SUKUN (last of the harakat removed)
    private const char SuperscriptAlef = '\u0670';  // ARABIC LETTER SUPERSCRIPT ALEF
    private const char AlefMadda = '\u0622';        // ARABIC LETTER ALEF WITH MADDA ABOVE
    private const char AlefHamzaAbove = '\u0623';   // ARABIC LETTER ALEF WITH HAMZA ABOVE
    private const char AlefHamzaBelow = '\u0625';   // ARABIC LETTER ALEF WITH HAMZA BELOW
    private const char Alef = '\u0627';             // ARABIC LETTER ALEF
    private const char AlefMaksura = '\u0649';      // ARABIC LETTER ALEF MAKSURA
    private const char Yeh = '\u064A';              // ARABIC LETTER YEH
    private const char TehMarbuta = '\u0629';       // ARABIC LETTER TEH MARBUTA
    private const char Heh = '\u0647';              // ARABIC LETTER HEH
    /// <summary>ARABIC LETTER LAM: also the preposition "to" written against the next word (<c>ContactResolver.ResolveAnswer</c>).</summary>
    internal const char Lam = '\u0644';

    /// <summary>
    /// NFC; drops Unicode format characters (zero-width joiner and non-joiner, zero-width space, bidi controls),
    /// tatweel and harakat; unifies alef forms, alef maksura and teh marbuta; digits to ASCII; trims and collapses
    /// whitespace; then maps each kinship variant to its canonical form.
    /// </summary>
    public static string Normalize(string text)
    {
        var letters = NormalizeLetters(text);
        if (letters.Length == 0)
            return letters;

        var words = letters.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (ArabicWords.KinshipVariants.TryGetValue(words[i], out var canonical))
                words[i] = canonical;
        }

        return string.Join(' ', words);
    }

    /// <summary>Drops a leading <c>al</c> (alef lam) of the whole string when at least two characters remain.</summary>
    public static string StripLeadingAl(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length >= 4 && text[0] == Alef && text[1] == Lam ? text[2..] : text;
    }

    /// <summary>
    /// The text with the preposition lam ("to") joined to its first word, as it is typed (milestone 9c, ruling 2 as
    /// amended): a first word that starts with alef lam loses that alef, so "to" and "the mother" are typed lam, lam,
    /// then the rest of the word; any other first word is typed lam, then the word ("to Ali" is lam, ain, lam, yeh).
    /// Only the plain alef is dropped. The text is trimmed first; nothing else is changed.
    /// </summary>
    /// <exception cref="ArgumentException">The text is blank.</exception>
    public static string LamForm(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var trimmed = text.Trim();
        return trimmed.Length >= 2 && trimmed[0] == Alef && trimmed[1] == Lam
            ? Lam + trimmed[1..]
            : Lam + trimmed;
    }

    /// <summary>The comparison key: normalised, leading <c>al</c> stripped, spaces removed.</summary>
    public static string Key(string text) => StripLeadingAl(Normalize(text)).Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>The words of the normalised text; empty when the text is blank.</summary>
    public static IReadOnlyList<string> Words(string text)
    {
        var normalized = Normalize(text);
        return normalized.Length == 0 ? [] : normalized.Split(' ');
    }

    /// <summary>True when the word is a title word of rule 1 (abu, umm).</summary>
    public static bool IsTitle(string word) => ArabicWords.Titles.Contains(NormalizeLetters(word));

    /// <summary>True when the word is a kinship word of rule 1 (any variant; variants map to their canonical form).</summary>
    public static bool IsKinship(string word) => ArabicWords.KinshipVariants.ContainsKey(NormalizeLetters(word));

    /// <summary>Levenshtein distance over UTF-16 code units: one letter added, dropped or changed counts one.</summary>
    public static int EditDistance(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>Every step of <see cref="Normalize"/> except the kinship mapping.</summary>
    internal static string NormalizeLetters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var composed = text.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(composed.Length);
        var pendingSpace = false;

        // Read as runes, so a format character outside the basic plane (two UTF-16 units) is seen as one.
        foreach (var rune in composed.EnumerateRunes())
        {
            // Invisible format characters would split a key or reorder the text on the card (category Cf).
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
                continue;

            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (!rune.IsBmp)
            {
                AppendPendingSpace(builder, ref pendingSpace);
                builder.Append(rune.ToString());
                continue;
            }

            var c = (char)rune.Value;
            if (c == Tatweel || c == SuperscriptAlef || c is >= FathatanFirst and <= SukunLast)
                continue;

            AppendPendingSpace(builder, ref pendingSpace);
            builder.Append(c switch
            {
                AlefHamzaAbove or AlefHamzaBelow or AlefMadda => Alef,
                AlefMaksura => Yeh,
                TehMarbuta => Heh,
                _ => ArabicDigits.ToAscii(c),
            });
        }

        return builder.ToString();
    }

    private static void AppendPendingSpace(StringBuilder builder, ref bool pendingSpace)
    {
        if (pendingSpace)
        {
            builder.Append(' ');
            pendingSpace = false;
        }
    }
}
