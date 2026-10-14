using Agent.Core.Learning;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Learning;

/// <summary>
/// What goes to the learning log (D12): one case per logged reason that a reply can carry, one per not-logged code, and
/// the pin of both lists to <c>data/seed/settings.json</c>. Pure: no database, no wallet.
/// </summary>
public class LearningLogRuleTests
{
    private static readonly ParsedRequest Unclear = new() { Intent = RequestIntent.Unclear };

    [Fact]
    public void TheListsAreTheSettingsLists()
    {
        var settings = SeedFacts.Seed.Settings.LearningLog;

        Assert.Equal(settings.Logged, LearningLogRule.Logged);
        Assert.Equal(settings.NotLogged, LearningLogRule.NotLogged);
    }

    [Theory]
    [InlineData(ReasonCodes.RecipientNotFound, "typed name")]
    [InlineData(ReasonCodes.BillNotFound, "water")]
    [InlineData(ReasonCodes.BillerUnavailable, null)]
    [InlineData(ReasonCodes.ProductNotFound, "korek credit 25000")]
    [InlineData(ReasonCodes.NotUnderstood, null)]
    [InlineData(ReasonCodes.NotSupported, ReasonCodes.OwnNumberDetail)]
    public void ALoggedRefusalIsLoggedWithItsDetail(string code, string? detail)
    {
        var entry = LearningLogRule.EntryFor(new AgentReply.Refusal(code, detail, null), Unclear);

        Assert.Equal(new LearningEntry(code, detail), entry);
        Assert.Contains(code, LearningLogRule.Logged);
    }

    [Fact]
    public void ABillNotFoundWithoutDetailTakesTheBillerAskedFor()
    {
        var entry = LearningLogRule.EntryFor(
            new AgentReply.Refusal(ReasonCodes.BillNotFound, null, null),
            ParsedRequest.Bill("water", null, null, AmountUnit.None));

        Assert.Equal(new LearningEntry(ReasonCodes.BillNotFound, "water"), entry);
    }

    [Fact]
    public void AProductNotFoundWithoutDetailTakesWhatWasAskedFor()
    {
        var entry = LearningLogRule.EntryFor(
            new AgentReply.Refusal(ReasonCodes.ProductNotFound, null, null),
            ParsedRequest.Purchase("korek", "credit", 25_000));

        Assert.Equal(new LearningEntry(ReasonCodes.ProductNotFound, "korek credit 25000"), entry);
    }

    [Theory]
    [InlineData(LearningLogRule.SameAsLastTime)]
    [InlineData(LearningLogRule.RefersBack)]
    [InlineData(LearningLogRule.ChangeWaitingCard)]
    [InlineData(LearningLogRule.ChooseAccount)]
    [InlineData(LearningLogRule.PartOfBill)]
    [InlineData(LearningLogRule.CreditToOwnNumber)]
    [InlineData(LearningLogRule.OtherCurrency)]
    [InlineData(LearningLogRule.OtherLanguage)]
    public void AnUnsupportedReasonWithItsOwnNameIsLoggedUnderIt(string reason)
    {
        var entry = LearningLogRule.EntryFor(new AgentReply.Unsupported(reason), Unsupported(reason));

        Assert.Equal(new LearningEntry(reason, null), entry);
        Assert.Contains(reason, LearningLogRule.Logged);
    }

    [Theory]
    [InlineData("withdrawal")]
    [InlineData("other_balance")]
    public void AnyOtherUnsupportedReasonIsNotSupportedWithTheReasonAsDetail(string reason)
    {
        var entry = LearningLogRule.EntryFor(new AgentReply.Unsupported(reason), Unsupported(reason));

        Assert.Equal(new LearningEntry(ReasonCodes.NotSupported, reason), entry);
    }

    [Fact]
    public void AnUnsupportedRequestWithoutAReasonIsNotSupportedWithoutDetail()
    {
        var entry = LearningLogRule.EntryFor(new AgentReply.Unsupported(ReasonCodes.NotSupported), Unsupported(null));

        Assert.Equal(new LearningEntry(ReasonCodes.NotSupported, null), entry);
    }

    [Fact]
    public void AnotherCurrencyIsLoggedAsOtherCurrency()
    {
        var request = ParsedRequest.Transfer("someone", 100, AmountUnit.None) with { CurrencyText = "dollars" };

        var entry = LearningLogRule.EntryFor(new AgentReply.Refusal(ReasonCodes.DinarsOnly, null, null), request);

        Assert.Equal(new LearningEntry(LearningLogRule.OtherCurrency, null), entry);
    }

    [Theory]
    [InlineData(ReasonCodes.InsufficientBalance)]
    [InlineData(ReasonCodes.OverPerTransactionCap)]
    [InlineData(ReasonCodes.OverDailyCap)]
    [InlineData(ReasonCodes.NothingDue)]
    [InlineData(ReasonCodes.AccountClosed)]
    [InlineData(ReasonCodes.NumberHasNoWallet)]
    [InlineData(ReasonCodes.ConsentRequired)]
    public void ANotLoggedRefusalIsNotLogged(string code)
    {
        Assert.Null(LearningLogRule.EntryFor(new AgentReply.Refusal(code, null, 1_000), Unclear));
        Assert.Contains(code, LearningLogRule.NotLogged);
    }

    [Fact]
    public void RepliesThatAreNotRefusalsAreNotLogged()
    {
        Assert.Null(LearningLogRule.EntryFor(new AgentReply.BalanceReply(750_000), ParsedRequest.Balance()));
        Assert.Null(LearningLogRule.EntryFor(new AgentReply.ConsentScreen(), ParsedRequest.Balance()));
        Assert.Null(LearningLogRule.EntryFor(new AgentReply.Question(QuestionKinds.WhichAmount, QuestionAnswer.Typed, []), Unclear));
    }

    [Fact]
    public void TheReasonsOnlyCallersKnowAreInTheLoggedList()
    {
        Assert.Contains(LearningLogRule.MoreThanThreeRequests, LearningLogRule.Logged);
        Assert.Contains(LearningLogRule.ModelInvalidReply, LearningLogRule.Logged);
    }

    private static ParsedRequest Unsupported(string? reason) => new() { Intent = RequestIntent.Unsupported, UnsupportedReason = reason };
}
