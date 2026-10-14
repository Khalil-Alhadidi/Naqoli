namespace Agent.Core.Text;

/// <summary>
/// Digit forms (D20): typed input is accepted with ASCII, Arabic-Indic or Eastern Arabic-Indic digits; the phone frame
/// shows Arabic-Indic digits.
/// </summary>
public static class ArabicDigits
{
    private const char ArabicIndicZero = '\u0660';        // ARABIC-INDIC DIGIT ZERO
    private const char ArabicIndicNine = '\u0669';        // ARABIC-INDIC DIGIT NINE
    private const char EasternArabicIndicZero = '\u06F0'; // EXTENDED ARABIC-INDIC DIGIT ZERO
    private const char EasternArabicIndicNine = '\u06F9'; // EXTENDED ARABIC-INDIC DIGIT NINE

    /// <summary>Maps Arabic-Indic and Eastern Arabic-Indic digits to ASCII; every other character is kept.</summary>
    public static string ToAscii(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = ToAscii(source[i]);
        });
    }

    /// <summary>Maps ASCII digits to Arabic-Indic digits; every other character is kept.</summary>
    public static string ToArabicIndic(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c is >= '0' and <= '9' ? (char)(ArabicIndicZero + (c - '0')) : c;
            }
        });
    }

    internal static char ToAscii(char c) => c switch
    {
        >= ArabicIndicZero and <= ArabicIndicNine => (char)('0' + (c - ArabicIndicZero)),
        >= EasternArabicIndicZero and <= EasternArabicIndicNine => (char)('0' + (c - EasternArabicIndicZero)),
        _ => c,
    };
}
