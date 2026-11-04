using Agent.Core.Learning;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The other-balance guard (Khalil, 4 Nov 2026, the prompt version 7 round): a balance question that names a person is
/// someone else's balance, refused whatever the model read (RED-10); everything else passes through. Names come from the
/// seed (<see cref="SeedFacts"/>), never as literals.
/// </summary>
public class OtherBalanceGuardTests
{
    private static string Name => SeedFacts.Contact("S01").SavedName;

    [Fact]
    public void ABalanceQuestionWithANameBecomesOtherBalance()
    {
        var guarded = OtherBalanceGuard.Apply([ParsedRequest.Balance() with { RecipientText = Name }]);

        var request = Assert.Single(guarded);
        Assert.Equal(RequestIntent.Unsupported, request.Intent);
        Assert.Equal(LearningLogRule.OtherBalance, request.UnsupportedReason);
        Assert.Equal(Name, request.RecipientText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ABalanceQuestionWithoutANameStays(string? recipient)
    {
        IReadOnlyList<ParsedRequest> requests = [ParsedRequest.Balance() with { RecipientText = recipient }];

        Assert.Same(requests, OtherBalanceGuard.Apply(requests));
    }

    [Fact]
    public void ATransferWithANameStays()
    {
        IReadOnlyList<ParsedRequest> requests = [ParsedRequest.Transfer(Name, 50, AmountUnit.Thousand)];

        Assert.Same(requests, OtherBalanceGuard.Apply(requests));
    }

    [Fact]
    public void AnUnsupportedRequestStays()
    {
        IReadOnlyList<ParsedRequest> requests =
        [
            new ParsedRequest { Intent = RequestIntent.Unsupported, UnsupportedReason = "withdrawal", RecipientText = Name },
        ];

        Assert.Same(requests, OtherBalanceGuard.Apply(requests));
    }

    [Fact]
    public void TheSameListIsReturnedWhenNothingChanges()
    {
        IReadOnlyList<ParsedRequest> requests = [ParsedRequest.Balance(), ParsedRequest.Transfer(Name, 50, AmountUnit.Thousand)];

        Assert.Same(requests, OtherBalanceGuard.Apply(requests));
    }

    [Fact]
    public void OnlyTheNamedBalanceQuestionOfSeveralChanges()
    {
        var transfer = ParsedRequest.Transfer(Name, 50, AmountUnit.Thousand);
        var guarded = OtherBalanceGuard.Apply([transfer, ParsedRequest.Balance() with { RecipientText = Name }]);

        Assert.Equal(2, guarded.Count);
        Assert.Same(transfer, guarded[0]);
        Assert.Equal((RequestIntent.Unsupported, LearningLogRule.OtherBalance), (guarded[1].Intent, guarded[1].UnsupportedReason));
    }
}
