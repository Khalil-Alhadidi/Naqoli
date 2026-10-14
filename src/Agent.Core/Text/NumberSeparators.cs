using System.Globalization;

namespace Agent.Core.Text;

/// <summary>
/// The one idea of what may stand between the digits of a typed number (review I1 of milestone 6), shared by the masker
/// (<c>Masking.Masker</c>), the phone rule (<see cref="PhoneNumbers"/>) and so the structure validator: any Unicode
/// whitespace (a no-break space, a tab, a line break), any format character (the bidi marks a phone keyboard inserts),
/// <c>-</c>, <c>.</c>, <c>(</c> and <c>)</c>. A comma is not one: it separates thousands in an amount (matching rule 19).
/// </summary>
public static class NumberSeparators
{
    /// <summary>The same set as a regular-expression character class (.NET <c>\s</c> is Unicode whitespace).</summary>
    public const string Pattern = @"[\s\p{Cf}\-.()]";

    /// <summary>True when <paramref name="c"/> may stand between the digits of a number.</summary>
    public static bool IsSeparator(char c)
        => char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format || c is '-' or '.' or '(' or ')';
}
