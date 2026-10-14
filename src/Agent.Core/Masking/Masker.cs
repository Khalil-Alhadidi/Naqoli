using System.Text.RegularExpressions;

namespace Agent.Core.Masking;

/// <summary>A message with its numbers masked, and the map that turns its tokens back into the numbers.</summary>
public sealed record MaskedText(string Text, MaskMap Map);

/// <summary>
/// Rule 5: phone and account numbers become tokens before text reaches the parse call. A number is a run of 7 or more
/// digits (ASCII, Arabic-Indic or Extended Arabic-Indic), with any spaces, dashes or dots between its digits counted
/// inside the run, and an optional leading <c>+</c>. Each run becomes <c>&lt;NUM_n&gt;</c>, n from 1 in order of
/// appearance; everything else is kept. An amount of six digits or fewer (<c>150000</c>, <c>1,000,000</c> with its
/// commas) is never masked; a seven-digit amount written without separators is (docs/failure-modes.md).
/// </summary>
public static partial class Masker
{
    /// <summary>The fewest digits a run needs to be masked.</summary>
    public const int MinDigits = 7;

    /// <summary>Masks every number in <paramref name="text"/>.</summary>
    public static MaskedText Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        var masked = NumberRun().Replace(text, match =>
        {
            var token = MaskMap.TokenFor(originals.Count + 1);
            originals.Add(token, match.Value);
            return token;
        });

        return originals.Count == 0
            ? new MaskedText(text, MaskMap.Empty)
            : new MaskedText(masked, new MaskMap(originals));
    }

    /// <summary>True when <paramref name="text"/> holds a run the masker would mask.</summary>
    public static bool HasNumberRun(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return NumberRun().IsMatch(text);
    }

    // A digit, then six or more digits each after any spaces, dashes or dots; the run starts and ends on a digit.
    [GeneratedRegex(@"\+?[0-9٠-٩۰-۹](?:[ \-.]*[0-9٠-٩۰-۹]){6,}", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRun();
}
