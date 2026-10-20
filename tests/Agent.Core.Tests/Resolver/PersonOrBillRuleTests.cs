using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Resolver;

/// <summary>
/// The person-or-bill rule (Khalil, 20 Oct 2026) on U1's seed contacts and bills: a name that is exactly both a saved name
/// or alias and a bill's label. The seed has no such pair, so each case adds the candidate it needs; every text comes from
/// the seed (BS2's label, S04's saved name), never a literal.
/// </summary>
public class PersonOrBillRuleTests
{
    private static readonly AgentOptions Options = new();

    private static string Label => SeedWallet.LabelOf("BS2");

    private static IReadOnlyList<ContactCandidate> U1 => SeedCandidates.For("U1");

    private static ContactCandidate S04 => U1.Single(c => c.ContactCode == "S04" && !c.IsAlias);

    private static ContactCandidate S05 => U1.Single(c => c.ContactCode == "S05" && !c.IsAlias);

    /// <summary>U1's candidates with <paramref name="text"/> added as an alias of S04's account.</summary>
    private static List<ContactCandidate> WithAliasOnS04(string text) => [.. U1, S04 with { Text = text, IsAlias = true }];

    [Fact]
    public void AnAliasEqualToABillLabelIsBothAPersonAndThatBill()
    {
        var match = Assert.IsType<PersonOrBillMatch>(PersonOrBillRule.Find(Label, WithAliasOnS04(Label), SeedWallet.BillsOf("U1"), Options));

        var person = Assert.Single(match.People);
        Assert.Equal(S04.AccountId, person.AccountId);
        Assert.Equal("BS2", Assert.Single(match.Bills).Code);
    }

    [Fact]
    public void ASavedNameEqualToABillLabelIsBothAPersonAndThatBill()
    {
        List<ContactCandidate> candidates = [.. U1.Where(c => c != S04), S04 with { Text = Label }];

        var match = Assert.IsType<PersonOrBillMatch>(PersonOrBillRule.Find(Label, candidates, SeedWallet.BillsOf("U1"), Options));

        Assert.Equal(S04.AccountId, Assert.Single(match.People).AccountId);
        Assert.Equal("BS2", Assert.Single(match.Bills).Code);
    }

    [Fact]
    public void ANearSpellingIsNotTheSameName()
    {
        // One letter more than the label: the resolver's near step, never its exact step.
        var near = Label + Label[^1];

        Assert.Null(PersonOrBillRule.Find(Label, WithAliasOnS04(near), SeedWallet.BillsOf("U1"), Options));
        Assert.Null(PersonOrBillRule.Find(near, WithAliasOnS04(Label), SeedWallet.BillsOf("U1"), Options));
    }

    [Fact]
    public void ASharedWordOrAPartialIsNotTheSameName()
    {
        // The label as one word of a longer alias: a shared word (and a partial) for the resolver, never exact.
        var longer = Label + " " + S04.Text.Split(' ')[0];

        Assert.Null(PersonOrBillRule.Find(Label, WithAliasOnS04(longer), SeedWallet.BillsOf("U1"), Options));
        Assert.Null(PersonOrBillRule.Find(longer, WithAliasOnS04(Label), SeedWallet.BillsOf("U1"), Options));
    }

    [Fact]
    public void WithNoBillsOrNoContactsNothingMatches()
    {
        Assert.Null(PersonOrBillRule.Find(Label, WithAliasOnS04(Label), [], Options));
        Assert.Null(PersonOrBillRule.Find(Label, [], SeedWallet.BillsOf("U1"), Options));
    }

    [Fact]
    public void TheSeedAsItIsHasNoSuchPair()
    {
        // Checked 20 Oct 2026: no saved name or alias of any user is exactly the label of one of that user's bills.
        foreach (var user in SeedFacts.Seed.Users)
        {
            var candidates = SeedCandidates.For(user.Id);
            foreach (var bill in SeedWallet.BillsOf(user.Id).Where(b => b.Label is not null))
                Assert.Null(PersonOrBillRule.Find(bill.Label!, candidates, SeedWallet.BillsOf(user.Id), Options));
        }
    }

    [Fact]
    public void TwoAccountsWithTheNameAreTwoPeople()
    {
        List<ContactCandidate> candidates = [.. WithAliasOnS04(Label), S05 with { Text = Label, IsAlias = true }];

        var match = Assert.IsType<PersonOrBillMatch>(PersonOrBillRule.Find(Label, candidates, SeedWallet.BillsOf("U1"), Options));

        Assert.Equal([S04.AccountId, S05.AccountId], match.People.Select(p => p.AccountId));
    }

    [Fact]
    public void TheTypedTextIsTheRecipientOfATransferAndTheLabelThenTheBillerOfABill()
    {
        Assert.Equal(Label, PersonOrBillRule.TypedText(ParsedRequest.Transfer(Label, 5, AmountUnit.Thousand)));
        Assert.Null(PersonOrBillRule.TypedText(ParsedRequest.Transfer(SeedFacts.HammoudiPhone, 5, AmountUnit.Thousand)));
        Assert.Null(PersonOrBillRule.TypedText(ParsedRequest.Transfer(null, 5, AmountUnit.Thousand)));
        Assert.Equal(Label, PersonOrBillRule.TypedText(ParsedRequest.Bill("electricity", Label, null, AmountUnit.None, "x")));
        Assert.Equal("x", PersonOrBillRule.TypedText(ParsedRequest.Bill("electricity", null, null, AmountUnit.None, "x")));
        Assert.Null(PersonOrBillRule.TypedText(ParsedRequest.Bill("electricity", null, null, AmountUnit.None)));
        Assert.Null(PersonOrBillRule.TypedText(ParsedRequest.Purchase(null, "internet", null)));
    }
}
