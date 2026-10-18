using Agent.Core.Aliases;
using Agent.Core.Resolver;
using Agent.Core.Tests.Resolver;
using Agent.Core.Tests.Wallet;
using Agent.Core.Text;

namespace Agent.Core.Tests.Aliases;

/// <summary>
/// Rule 18 (milestone 9a, ruling 2): an alias is offered after a question from the near step (rule 6), the shared-word
/// step (rule 15) or the title-only step (rule 5), never after a bare name shared by several (rules 3 and 4), and never
/// for a text the save would refuse as existing or shared (Khalil, 18 Oct 2026). The typed names come from the matching
/// examples, tagged by rule, from AMB-19 in the development set and from the seed; none is a literal.
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
            Assert.True(AliasRule.StepOffers(Question(example.Typed, example.User)), $"rule {rule}: no offer for an example");
    }

    [Theory]
    [InlineData("AMB-19")]
    [InlineData("AMB-19-v1")]
    public void OffersForTheRecipientOfAmb19(string id)
    {
        // The text the case expects offered is the recipient as the user typed it (ruling 3).
        var typed = DevSetText.AliasOffer(id, FirstConfirmStep);
        Assert.NotNull(typed);

        Assert.True(AliasRule.Offers(typed.Trim(), Question(typed, "U1"), SeedCandidates.For("U1")));
    }

    [Fact]
    public void AOneWordTextTheNearStepAsksAboutOnSeveralAccountsGetsNoOffer()
    {
        // S08 and S09 have the same saved name, one word of four letters; with its last letter dropped the text is one
        // edit from both, so the near step asks on two accounts. Saved, it would go straight to one: alias_shared.
        var savedName = SeedFacts.Contact("S08").SavedName;
        Assert.Equal(savedName, SeedFacts.Contact("S09").SavedName);
        Assert.Single(ArabicText.Words(savedName));
        var typed = savedName[..^1];
        var candidates = SeedCandidates.For("U1");

        var askedBy = Question(typed, "U1");
        Assert.Equal(MatchStep.Near, askedBy.Step);
        Assert.Equal(2, askedBy.Candidates.Count);
        Assert.True(AliasRule.StepOffers(askedBy));

        Assert.False(AliasRule.Offers(typed, askedBy, candidates));
    }

    [Fact]
    public void ATwoWordTextThatSharesAWordWithSeveralSavedNamesIsOffered()
    {
        // As AliasDirectoryTests builds it: S04's first word, then the last word of another of U1's saved names that
        // holds neither word of S04's name; the shared-word step asks on several accounts, and two words are never shared.
        var candidates = SeedCandidates.For("U1");
        var s04Words = SeedFacts.Contact("S04").SavedName.Split(' ');
        var asked = SeedFacts.Seed.Contacts
            .Where(c => c.User == "U1" && c.Id != "S04")
            .Select(c => c.SavedName.Split(' '))
            .Where(words => words.Length > 1 && !words.Intersect(s04Words, StringComparer.Ordinal).Any())
            .Select(words => s04Words[0] + " " + words[^1])
            .Select(text => (Text: text, Resolution: ContactResolver.Resolve(text, candidates)))
            .FirstOrDefault(t => t.Resolution is Resolution.Options { Step: MatchStep.SharedWord, Candidates.Count: > 1 });
        Assert.NotNull(asked.Text);

        Assert.True(AliasRule.Offers(asked.Text, Assert.IsType<Resolution.Options>(asked.Resolution), candidates));
    }

    [Fact]
    public void ATextThatIsAlreadyASavedNameIsNotOffered()
    {
        // alias_exists: the key of the text equals the key of a saved name. Two words, so the shared check is not what
        // refuses it; the question is built by hand, as the resolver would answer such a text exactly.
        var candidates = SeedCandidates.For("U1");
        var saved = candidates.First(c => !c.IsAlias && ArabicText.Words(c.Text).Count > 1);
        var askedBy = new Resolution.Options(QuestionKind.DidYouMean, candidates.Take(2).ToList(), MatchStep.Near);

        Assert.True(AliasRule.StepOffers(askedBy));
        Assert.False(AliasRule.Offers(saved.Text, askedBy, candidates));
    }

    [Theory]
    [MemberData(nameof(BareNameRules))]
    public void NeverOffersABareNameSharedBySeveral(int rule)
    {
        var examples = MatchingExample.QuestionsOf(rule).ToList();
        Assert.NotEmpty(examples);

        foreach (var example in examples)
            Assert.False(AliasRule.StepOffers(Question(example.Typed, example.User)), $"rule {rule}: an offer for a bare name");
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

        Assert.Equal(offered, AliasRule.StepOffers(new Resolution.Options(QuestionKind.WhichContact, candidates, step)));
        Assert.Equal(offered, AliasRule.StepOffers(new Resolution.Options(QuestionKind.DidYouMean, candidates, step)));
    }

    private static Resolution.Options Question(string typed, string user)
        => Assert.IsType<Resolution.Options>(ContactResolver.Resolve(typed, SeedCandidates.For(user)), exactMatch: true);
}
