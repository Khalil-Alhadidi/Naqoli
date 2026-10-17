using Agent.Core.Aliases;
using Agent.Core.Resolver;
using Agent.Core.Tests.Resolver;

namespace Agent.Core.Tests.Aliases;

/// <summary>
/// Rule 18 (milestone 9a, ruling 2): an alias is offered after a question from the near step (rule 6), the shared-word
/// step (rule 15) or the title-only step (rule 5), never after a bare name shared by several (rules 3 and 4). The typed
/// names come from the matching examples, tagged by rule, and from AMB-19 in the development set; none is a literal.
/// </summary>
public class AliasRuleTests
{
    /// <summary>The steps of AMB-19 and AMB-19-v1 that expect an offer (the first confirm).</summary>
    private const int FirstConfirmStep = 2;

    public static TheoryData<int> OfferingRules() => [5, 6, 15];

    public static TheoryData<int> BareNameRules() => [3, 4];

    [Theory]
    [MemberData(nameof(OfferingRules))]
    public void OffersAfterANearSharedWordOrTitleQuestion(int rule)
    {
        var examples = MatchingExample.QuestionsOf(rule).ToList();
        Assert.NotEmpty(examples);

        foreach (var example in examples)
            Assert.True(AliasRule.Offers(Question(example.Typed, example.User)), $"rule {rule}: no offer for an example");
    }

    [Theory]
    [InlineData("AMB-19")]
    [InlineData("AMB-19-v1")]
    public void OffersForTheRecipientOfAmb19(string id)
    {
        // The text the case expects offered is the recipient as the user typed it (ruling 3).
        var typed = DevSetText.AliasOffer(id, FirstConfirmStep);
        Assert.NotNull(typed);

        Assert.True(AliasRule.Offers(Question(typed, "U1")));
    }

    [Theory]
    [MemberData(nameof(BareNameRules))]
    public void NeverOffersABareNameSharedBySeveral(int rule)
    {
        var examples = MatchingExample.QuestionsOf(rule).ToList();
        Assert.NotEmpty(examples);

        foreach (var example in examples)
            Assert.False(AliasRule.Offers(Question(example.Typed, example.User)), $"rule {rule}: an offer for a bare name");
    }

    [Theory]
    [InlineData(MatchStep.Near, true)]
    [InlineData(MatchStep.SharedWord, true)]
    [InlineData(MatchStep.TitleOnly, true)]
    [InlineData(MatchStep.Exact, false)]
    [InlineData(MatchStep.Partial, false)]
    public void TheRuleReadsTheStepOnly(MatchStep step, bool offered)
    {
        var candidates = SeedCandidates.For("U1").Take(2).ToList();

        Assert.Equal(offered, AliasRule.Offers(new Resolution.Options(QuestionKind.WhichContact, candidates, step)));
        Assert.Equal(offered, AliasRule.Offers(new Resolution.Options(QuestionKind.DidYouMean, candidates, step)));
    }

    private static Resolution.Options Question(string typed, string user)
        => Assert.IsType<Resolution.Options>(ContactResolver.Resolve(typed, SeedCandidates.For(user)), exactMatch: true);
}
