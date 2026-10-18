using Agent.Core.Resolver;

namespace Agent.Core.Aliases;

/// <summary>
/// Rule 18 (milestone 9a): after a completed payment whose recipient needed a question, the agent offers to save what
/// the user typed as an alias, unless the typed text was a bare name shared by several contacts (ruling 2), and unless
/// the save itself would refuse it (Khalil, 18 Oct 2026): the offer runs the save's <see cref="AliasChecks"/> first.
/// Pure.
/// </summary>
public static class AliasRule
{
    /// <summary>
    /// Rule 18: <see cref="StepOffers"/> on <paramref name="askedBy"/>, and neither <see cref="AliasChecks.Exists"/> nor
    /// <see cref="AliasChecks.IsShared"/> refuses <paramref name="typedText"/> over <paramref name="candidates"/>.
    /// </summary>
    /// <param name="typedText">The recipient as the user typed it, trimmed.</param>
    /// <param name="askedBy">The resolution that asked the question, over <paramref name="candidates"/>.</param>
    /// <param name="candidates">The user's saved names and aliases the question was resolved against.</param>
    public static bool Offers(string typedText, Resolution.Options askedBy, IReadOnlyList<ContactCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(typedText);
        ArgumentNullException.ThrowIfNull(askedBy);
        ArgumentNullException.ThrowIfNull(candidates);

        return StepOffers(askedBy)
            && !AliasChecks.Exists(candidates, typedText)
            && !AliasChecks.IsShared(typedText, askedBy);
    }

    /// <summary>
    /// The step part of rule 18: true when the question came from the near step (rule 6), the shared-word step (rule 15)
    /// or the title-only step (rule 5); false for an exact hit on several accounts (rule 3) or a partial hit on several
    /// (rule 4).
    /// </summary>
    public static bool StepOffers(Resolution.Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Step switch
        {
            MatchStep.Near or MatchStep.SharedWord or MatchStep.TitleOnly => true,
            MatchStep.Exact or MatchStep.Partial => false,
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Step, "Unknown matching step."),
        };
    }
}
