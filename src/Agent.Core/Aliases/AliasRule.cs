using Agent.Core.Resolver;

namespace Agent.Core.Aliases;

/// <summary>
/// Rule 18 (milestone 9a, ruling 2): after a completed payment whose recipient needed a question, the agent offers to
/// save what the user typed as an alias, unless the typed text was a bare name shared by several contacts. Read from
/// the step that asked the question. Pure.
/// </summary>
public static class AliasRule
{
    /// <summary>
    /// True when the question came from the near step (rule 6), the shared-word step (rule 15) or the title-only step
    /// (rule 5); false for an exact hit on several accounts (rule 3) or a partial hit on several (rule 4).
    /// </summary>
    public static bool Offers(Resolution.Options options)
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
