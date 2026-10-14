using System.Text;
using System.Text.RegularExpressions;

namespace Agent.Core.Text;

/// <summary>
/// Matching rule 20: the wallet is local, so a country code makes no difference; spaces and dashes are ignored; what is
/// left must be 11 digits starting with 07. A number is never completed or matched to a similar one.
/// </summary>
public static partial class PhoneNumbers
{
    private const int LooksLikeNumberMinDigits = 7;

    /// <summary>Normalises a typed phone number to <c>07XXXXXXXXX</c>; false (and an empty phone) when it is not complete.</summary>
    public static bool TryNormalize(string? typed, out string phone)
    {
        phone = string.Empty;
        if (string.IsNullOrWhiteSpace(typed))
            return false;

        var digits = StripSeparators(typed);
        if (digits.StartsWith('+'))
            digits = digits[1..];

        if (digits.StartsWith("00964", StringComparison.Ordinal))
            digits = "0" + digits[5..];
        else if (digits.StartsWith("964", StringComparison.Ordinal))
            digits = "0" + digits[3..];

        if (!LocalNumber().IsMatch(digits))
            return false;

        phone = digits;
        return true;
    }

    /// <summary>
    /// True when the text is a phone number attempt (7 or more digits, with only separators between them
    /// (<see cref="NumberSeparators"/>) and an optional leading plus), so the recipient goes to the phone path rather than
    /// the name resolver.
    /// </summary>
    public static bool LooksLikeNumber(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return false;

        var digits = StripSeparators(typed);
        if (digits.StartsWith('+'))
            digits = digits[1..];

        return digits.Length >= LooksLikeNumberMinDigits && digits.All(char.IsAsciiDigit);
    }

    private static string StripSeparators(string typed)
    {
        var builder = new StringBuilder(typed.Length);
        foreach (var c in ArabicDigits.ToAscii(typed.Trim()))
        {
            if (NumberSeparators.IsSeparator(c))
                continue;
            builder.Append(c);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^07[0-9]{9}\z", RegexOptions.CultureInvariant)]
    private static partial Regex LocalNumber();
}
