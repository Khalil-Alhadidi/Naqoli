using Agent.Core.Pipeline;
using Agent.Core.Resolver;
using Agent.Core.Text;

namespace Agent.Core.Aliases;

/// <summary>
/// The two pure checks a save of an alias runs over the user's candidates (milestone 9a, ruling 4 and review R4), shared
/// by <see cref="AliasDirectory"/>, which refuses a save on them, and <see cref="AliasRule.Offers(string, Resolution.Options, IReadOnlyList{ContactCandidate})"/>,
/// which makes no offer that the save would refuse (Khalil, 18 Oct 2026). No I/O.
/// </summary>
public static class AliasChecks
{
    /// <summary>
    /// <see cref="ReasonCodes.AliasExists"/>: the key of <paramref name="text"/> (<see cref="ArabicText.Key"/>) equals the
    /// key of a saved name or alias the user already has, on any account.
    /// </summary>
    public static bool Exists(IReadOnlyList<ContactCandidate> candidates, string text)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(text);

        var key = ArabicText.Key(text);
        return candidates.Any(c => string.Equals(ArabicText.Key(c.Text), key, StringComparison.Ordinal));
    }

    /// <summary>
    /// <see cref="ReasonCodes.AliasShared"/> (review R4, Khalil, 18 Oct 2026): <paramref name="text"/> is one word and
    /// <paramref name="resolution"/>, the resolver's answer to it over the user's candidates, is a question on more than
    /// one account (the partial step on several, rule 4; the shared-word step, rule 15; or the near step on several).
    /// Saved, it would go straight to one account where the user would have been asked. A text of two or more words is
    /// never shared.
    /// </summary>
    public static bool IsShared(string text, Resolution resolution)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(resolution);

        return ArabicText.Words(text).Count == 1 && resolution is Resolution.Options { Candidates.Count: > 1 };
    }
}
