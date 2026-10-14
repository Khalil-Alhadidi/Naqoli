using System.Globalization;
using System.Text.RegularExpressions;

namespace Agent.Core.Masking;

/// <summary>
/// The tokens of one message and the numbers they stand for (rule 5). Only the map of the message a token came from
/// can turn it back into the number, so a token the parse call invents, or one from another message, stays a token.
/// </summary>
public sealed partial class MaskMap
{
    private readonly Dictionary<string, string> originals;

    internal MaskMap(Dictionary<string, string> originals) => this.originals = originals;

    /// <summary>The map of a message with no number in it.</summary>
    public static MaskMap Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>How many numbers were masked.</summary>
    public int Count => originals.Count;

    /// <summary>The token of the <paramref name="position"/>-th number of a message, from 1: <c>&lt;NUM_1&gt;</c>.</summary>
    internal static string TokenFor(int position) => "<NUM_" + position.ToString(CultureInfo.InvariantCulture) + ">";

    /// <summary>True when <paramref name="text"/> is exactly one token, with nothing around it.</summary>
    public static bool IsToken(string? text) => text is not null && ExactToken().IsMatch(text);

    /// <summary>
    /// The number <paramref name="token"/> stands for, exactly as typed; false (and an empty string) for anything that
    /// is not a token of this map.
    /// </summary>
    public bool TryUnmask(string token, out string original)
    {
        if (token is not null && originals.TryGetValue(token, out var found))
        {
            original = found;
            return true;
        }

        original = string.Empty;
        return false;
    }

    /// <summary>True when every token inside <paramref name="text"/> is one of this map's.</summary>
    public bool KnowsEveryToken(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (Match match in AnyToken().Matches(text))
        {
            if (!originals.ContainsKey(match.Value))
                return false;
        }

        return true;
    }

    [GeneratedRegex(@"^<NUM_[1-9][0-9]*>\z", RegexOptions.CultureInvariant)]
    private static partial Regex ExactToken();

    [GeneratedRegex("<NUM_[0-9]+>", RegexOptions.CultureInvariant)]
    private static partial Regex AnyToken();
}
