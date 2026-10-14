using System.Text.Json;
using Agent.Core.Resolver;
using Agent.Core.Text;
using Agent.Core.Tests.Wallet;
using Agent.Core.Wallet;

namespace Agent.Core.Tests.Resolver;

/// <summary>
/// The contact resolver against every example of matching rules 1 to 8 and 15 (<c>matching-examples.json</c>,
/// transcribed from <c>docs/matching-rules.md</c>), over candidates built from the seed.
/// </summary>
public class ContactResolverTests
{
    private sealed record MatchingExample(
        int Rule,
        string User,
        string Typed,
        string Expect,
        string? AccountOf,
        string? Kind,
        string[]? Options,
        string? Pending);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static IReadOnlyList<MatchingExample> Examples { get; } = JsonSerializer.Deserialize<List<MatchingExample>>(
        File.ReadAllText(Path.Combine(SeedHashTests.RepositoryRoot(), "tests", "Agent.Core.Tests", "Resolver", "matching-examples.json")),
        JsonOptions)!;

    public static TheoryData<int> ExampleIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Examples.Count; i++)
            data.Add(i);
        return data;
    }

    [Fact]
    public void TheExamplesCoverEveryNameRule()
        => Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 15], Examples.Select(e => e.Rule).Distinct().Order());

    [Theory]
    [MemberData(nameof(ExampleIndexes))]
    public void EveryMatchingExamplePasses(int index)
    {
        var example = Examples[index];
        if (example.Pending is not null)
            Assert.Skip($"Rule {example.Rule}: {example.Pending}");

        var candidates = SeedCandidates.For(example.User);
        var resolution = ContactResolver.Resolve(example.Typed, candidates);
        var label = $"example {index} (rule {example.Rule}, {example.User})";

        switch (example.Expect)
        {
            case "card":
                var card = Assert.IsType<Resolution.Card>(resolution, exactMatch: true);
                Assert.True(
                    SeedFacts.Contact(example.AccountOf!).Number == card.Chosen.Phone,
                    $"{label}: card went to {card.Chosen.ContactCode}, expected the account of {example.AccountOf}");
                break;

            case "options":
                var options = Assert.IsType<Resolution.Options>(resolution, exactMatch: true);
                Assert.Equal(example.Kind, KindName(options.Kind));
                Assert.Equal(example.Options!.Order(StringComparer.Ordinal), options.Candidates.Select(c => c.ContactCode!).Order(StringComparer.Ordinal));
                break;

            case "not_found":
                Assert.IsType<Resolution.NotFound>(resolution, exactMatch: true);
                break;

            default:
                Assert.Fail($"{label}: unknown expectation {example.Expect}");
                break;
        }
    }

    [Fact]
    public void AliasBeatsSavedNameAsDisplay()
    {
        // S12 (alias) and S13 (saved name) are the same account; the last word of S13's saved name is inside both.
        var candidates = SeedCandidates.For("U1");
        var typed = SeedFacts.Contact("S13").SavedName.Split(' ')[^1];
        var savedFirst = candidates.Where(c => !c.IsAlias).Concat(candidates.Where(c => c.IsAlias)).ToList();

        var card = Assert.IsType<Resolution.Card>(ContactResolver.Resolve(typed, savedFirst), exactMatch: true);

        Assert.True(card.Chosen.IsAlias);
        Assert.Equal(SeedFacts.Contact("S12").Alias, card.Chosen.Text);
    }

    [Fact]
    public void ExactAliasDisplaysTheAlias()
    {
        var card = Assert.IsType<Resolution.Card>(
            ContactResolver.Resolve(SeedFacts.Contact("S01").Alias!, SeedCandidates.For("U1")), exactMatch: true);

        Assert.True(card.Chosen.IsAlias);
        Assert.Equal("S01", card.Chosen.ContactCode);
    }

    [Fact]
    public void ClosedAccountStaysACandidate()
    {
        var s20 = SeedFacts.Contact("S20");

        var card = Assert.IsType<Resolution.Card>(ContactResolver.Resolve(s20.SavedName, SeedCandidates.For("U1")), exactMatch: true);

        Assert.Equal("closed", card.Chosen.AccountStatus);
        Assert.Equal(s20.Number, card.Chosen.Phone);
    }

    [Fact]
    public void TitleOnlyTypedNameIsNotFound()
    {
        var candidates = SeedCandidates.For("U1");

        // The title word that opens S20's saved name, and the kinship word that opens S01's alias, typed alone.
        Assert.IsType<Resolution.NotFound>(ContactResolver.Resolve(SeedFacts.Contact("S20").SavedName.Split(' ')[0], candidates), exactMatch: true);
        Assert.IsType<Resolution.NotFound>(ContactResolver.Resolve(SeedFacts.Contact("S01").Alias!.Split(' ')[0], candidates), exactMatch: true);
    }

    [Fact]
    public void ExactBareKinshipAliasStillGivesACard()
    {
        // U2 saved Z01 with the bare kinship word as alias: the exact step runs before the title/kinship-only guard.
        var card = Assert.IsType<Resolution.Card>(
            ContactResolver.Resolve(SeedFacts.Contact("Z01").Alias!, SeedCandidates.For("U2")), exactMatch: true);

        Assert.Equal("Z01", card.Chosen.ContactCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTypedNameIsNotFound(string typed)
        => Assert.IsType<Resolution.NotFound>(ContactResolver.Resolve(typed, SeedCandidates.For("U1")), exactMatch: true);

    [Fact]
    public void FromWalletContactsGivesOneCandidatePerSavedNameAndAlias()
    {
        var sent = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(3));
        var contacts = new WalletContacts(
            [
                new WalletContact(1, "S01", "saved one", "07805550101", "Reg One", "active", 11, sent, 50_000),
                new WalletContact(2, "S02", "saved two", "07805550102", null, "closed", 12, null, null),
            ],
            [
                new WalletAlias(7, "alias one", 11, "07805550101", "Reg One", "active"),
                new WalletAlias(8, "orphan", 99, "07805550199", null, "active"),
            ]);

        var candidates = ContactCandidate.From(contacts);

        Assert.Equal(4, candidates.Count);
        Assert.Equal(new ContactCandidate("saved one", false, 11, "07805550101", "Reg One", "active", "S01", sent, 50_000), candidates[0]);
        Assert.Equal(new ContactCandidate("saved two", false, 12, "07805550102", null, "closed", "S02", null, null), candidates[1]);
        Assert.Equal(new ContactCandidate("alias one", true, 11, "07805550101", "Reg One", "active", "S01", sent, 50_000), candidates[2]);
        Assert.Equal(new ContactCandidate("orphan", true, 99, "07805550199", null, "active", null, null, null), candidates[3]);
    }

    [Fact]
    public void TwoLetterFragmentOfASavedNameIsNotFound()
    {
        // The first two letters of S01's first saved-name word: too short to match inside a longer word.
        var fragment = SeedFacts.Contact("S01").SavedName.Split(' ')[0][..2];

        Assert.IsType<Resolution.NotFound>(ContactResolver.Resolve(fragment, SeedCandidates.For("U1")), exactMatch: true);
    }

    [Fact]
    public void TwoLetterWholeWordOfOneCandidateGivesACard()
    {
        // A short word (not a title or kinship word) that is a whole word of exactly one account's candidates in U1's
        // directory. U1's seed has none today; the test skips until one exists.
        var candidates = SeedCandidates.For("U1");
        var word = candidates
            .SelectMany(c => ArabicText.Words(c.Text).Select(w => (Word: ArabicText.StripLeadingAl(w), c.AccountId)))
            .Where(x => x.Word.Length < ContactResolver.MinSubstringWordLength && !ArabicText.IsTitle(x.Word) && !ArabicText.IsKinship(x.Word))
            .GroupBy(x => x.Word, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.AccountId).Distinct().Count() == 1)
            .Select(g => g.Key)
            .FirstOrDefault();
        if (word is null)
            Assert.Skip("U1's directory has no two-letter name word on exactly one account.");

        var card = Assert.IsType<Resolution.Card>(ContactResolver.Resolve(word, candidates), exactMatch: true);
        Assert.Contains(word, ArabicText.Words(card.Chosen.Text).Select(ArabicText.StripLeadingAl));
    }

    [Fact]
    public void TheMinimumIsPassedIn()
    {
        // The first three letters of S01's first saved-name word match inside it with the default minimum of three,
        // and must be a whole word (so are not found) with a minimum of four.
        var fragment = SeedFacts.Contact("S01").SavedName.Split(' ')[0][..3];
        var candidates = SeedCandidates.For("U1");

        Assert.IsNotType<Resolution.NotFound>(ContactResolver.Resolve(fragment, candidates, minSubstringWordLength: 3), exactMatch: true);
        Assert.IsType<Resolution.NotFound>(ContactResolver.Resolve(fragment, candidates, minSubstringWordLength: 4), exactMatch: true);
    }

    [Fact]
    public void TheDefaultMinimumIsTheSetting()
        => Assert.Equal(ContactResolver.MinSubstringWordLength, new AgentOptions().MinSubstringWordLength);

    private static string KindName(QuestionKind kind) => kind switch
    {
        QuestionKind.WhichContact => "which_contact",
        QuestionKind.DidYouMean => "did_you_mean",
        _ => kind.ToString(),
    };
}
