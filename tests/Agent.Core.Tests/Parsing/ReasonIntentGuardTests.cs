using Agent.Core.Learning;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The reason guard (the milestone 11 preparation, 5 Nov 2026): a request that carries an unsupported reason is
/// unsupported whatever intent the model gave (GAP-04 read as a transfer with <c>choose_account</c>), every slot kept;
/// everything else passes through. Names come from the seed (<see cref="SeedFacts"/>), never as literals.
/// </summary>
public class ReasonIntentGuardTests
{
    private static string Name => SeedFacts.Contact("S01").SavedName;

    [Fact]
    public void ATransferWithChooseAccountBecomesUnsupportedWithItsSlotsKept()
    {
        var transfer = ParsedRequest.Transfer(Name, 50, AmountUnit.Thousand) with { UnsupportedReason = LearningLogRule.ChooseAccount };

        var request = Assert.Single(ReasonIntentGuard.Apply([transfer]));

        Assert.Equal(transfer with { Intent = RequestIntent.Unsupported }, request);
    }

    [Fact]
    public void ATransferWithReceiveFromBecomesUnsupported()
    {
        var transfer = ParsedRequest.Transfer(Name, 50, AmountUnit.Thousand) with { UnsupportedReason = LearningLogRule.ReceiveFrom };

        var request = Assert.Single(ReasonIntentGuard.Apply([transfer]));

        Assert.Equal((RequestIntent.Unsupported, LearningLogRule.ReceiveFrom, Name), (request.Intent, request.UnsupportedReason, request.RecipientText));
    }

    [Fact]
    public void ARequestWithNoReasonStays()
    {
        IReadOnlyList<ParsedRequest> requests = [ParsedRequest.Transfer(Name, 50, AmountUnit.Thousand), ParsedRequest.Balance()];

        Assert.Same(requests, ReasonIntentGuard.Apply(requests));
    }

    [Fact]
    public void AnUnsupportedRequestStays()
    {
        IReadOnlyList<ParsedRequest> requests =
        [
            new ParsedRequest { Intent = RequestIntent.Unsupported, UnsupportedReason = LearningLogRule.ChooseAccount, RecipientText = Name },
        ];

        Assert.Same(requests, ReasonIntentGuard.Apply(requests));
    }

    [Fact]
    public void OnlyTheRequestWithAReasonOfSeveralChanges()
    {
        var plain = ParsedRequest.Transfer(Name, 50, AmountUnit.Thousand);
        var flagged = plain with { UnsupportedReason = LearningLogRule.OtherLanguage };

        var guarded = ReasonIntentGuard.Apply([plain, flagged]);

        Assert.Equal(2, guarded.Count);
        Assert.Same(plain, guarded[0]);
        Assert.Equal((RequestIntent.Unsupported, LearningLogRule.OtherLanguage), (guarded[1].Intent, guarded[1].UnsupportedReason));
    }
}
