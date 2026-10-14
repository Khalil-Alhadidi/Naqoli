using Agent.Core.Learning;
using Agent.Core.Parsing;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Eval.Cases;
using Agent.Eval.Running;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Eval;

/// <summary>
/// The runner's case driver against the agent host's in-process wallet: <c>AddAgentEval</c> over the host's
/// configuration, a <see cref="WalletAdmin"/> on the wallet's own client, and cases built here from English placeholder
/// sentences (runner fixtures, not answer keys).
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class CaseDriverTests(AgentHost host) : IAsyncDisposable
{
    private const string HammoudiSentence = "send fifty thousand to my brother Hammoudi";
    private const string TraderSentence = "pay the trader";
    private const string ModelSentence = "something for the model";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly List<IAsyncDisposable> disposables = [];

    public async ValueTask DisposeAsync()
    {
        for (var i = disposables.Count - 1; i >= 0; i--)
            await disposables[i].DisposeAsync();
    }

    private sealed record Runner(CaseDriver Driver, EvalClock Clock, RecordingParser Parser);

    private Runner Start()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);

        var provider = host.BuildProvider((services, configuration) => services.AddAgentEval(configuration));
        disposables.Add(provider);
        var http = host.Wallet.CreateClient();
        disposables.Add(new Disposer(http.Dispose));

        var pins = SeedPins.Load(SeedSetTests.SeedDirectory);
        var admin = new WalletAdmin(http, WalletApiFixture.DevelopmentAdminHeaderValue, pins);
        var parser = new RecordingParser(new ScriptedParser(
        [
            new ScriptedSentence(HammoudiSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand)),
            new ScriptedSentence(TraderSentence, ParsedRequest.Transfer(SeedFacts.Contact("T01").SavedName, 1_100_000, AmountUnit.Dinars)),
        ]));
        var clock = provider.GetRequiredService<EvalClock>();
        var options = new EvalOptions { SettleDelayMs = 50 };
        return new Runner(new CaseDriver(provider, admin, pins, clock, parser, options), clock, parser);
    }

    private static EvalCase Case(string user, CaseFault? fault, params CaseStep[] steps)
        => new("T-" + Guid.NewGuid().ToString("N")[..6], "dev", "Clean", "Transfer", user, steps[0].Say ?? string.Empty, steps, fault, new Expectation());

    private static CaseStep Say(string text) => new(Say: text);

    private static CaseStep Confirm(string? pin = null, bool twice = false) => new(Tap: "confirm", Pin: pin, Twice: twice);

    [Fact]
    public void AStepErrorKeepsTheExceptionTypeAndNeverItsMessage()
    {
        // A model SDK's exception may echo the input (review M1), so the message never reaches the report.
        var error = CaseDriver.StepErrorOf(new InvalidOperationException("send fifty to 07701234567"));

        Assert.Equal("InvalidOperationException: " + CaseDriver.StepErrorMessage, error);
        Assert.DoesNotContain("0770", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRecordedParserTextIsMasked()
    {
        // Review R3: the recording sits behind the conversation's masker, so the outbound check sees what the model sees.
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(Case("U1", null, Say("send 50000 to " + SeedFacts.HammoudiPhone)), Token);

        Assert.Equal(["send 50000 to <NUM_1>"], seen.ParserTexts);
    }

    [Fact]
    public async Task APinTypedIntoTheChatIsSeenInTheParserTextAndTheLearningLog()
    {
        // Review I2: a typed PIN (four digits, not masked) reaches the parse call and the learning row of the not_understood
        // reply; the grader sees both. The next case starts with an empty learning log.
        var runner = Start();
        var pin = SeedFacts.PinOf("U1");

        var typed = await runner.Driver.PlayAsync(Case("U1", null, Say("my pin is " + pin)), Token);
        var clean = await runner.Driver.PlayAsync(Case("U1", null, Say("something unknown")), Token);

        Assert.True(typed.Steps[0].PinSeen);
        Assert.False(clean.Steps[0].PinSeen);
    }

    [Fact]
    public async Task ACleanTransferIsPlayedEndToEnd()
    {
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(Case("U1", null, Say(HammoudiSentence), Confirm()), Token);

        Assert.Null(seen.StepError);
        Assert.Null(seen.UnsupportedStep);
        var card = Assert.Single(seen.Cards);
        Assert.Equal((50_000L, 1_000L, 699_000L, "done"), (card.View.AmountIqd, card.View.FeeIqd, card.View.BalanceAfterIqd, card.FinalState));
        Assert.Equal(2, seen.Steps.Count);
        Assert.True(seen.Steps[0].CardIssued);
        Assert.Equal(("live", 0L), (seen.Steps[0].CardState, seen.Steps[0].MovedIqd));
        Assert.Equal(("done", "done", 1, 51_000L), (seen.Steps[1].CardState, seen.Steps[1].OutcomeCode, seen.Steps[1].Executions, seen.Steps[1].MovedIqd));

        Assert.Equal(SeedFacts.DemoBalance, seen.WalletBefore.BalanceIqd);
        var transaction = Assert.Single(seen.WalletDiff.NewTransactions);
        Assert.Equal(("transfer", 50_000L, 1_000L, SeedFacts.HammoudiPhone), (transaction.Type, transaction.AmountIqd, transaction.FeeIqd, transaction.ToPhone));
        Assert.Equal((699_000L, 0), (seen.WalletDiff.BalanceIqd, seen.WalletDiff.CodesIssued));
        Assert.Equal([HammoudiSentence], seen.ParserTexts);
        Assert.Equal((0, 0, 0m), (seen.Usage.InputTokens, seen.Usage.OutputTokens, seen.Usage.CostUsd));
        Assert.Equal(["live", "done"], seen.StatusSequence);
        Assert.Equal(1, seen.ConfirmTaps);
        Assert.Null(seen.LearningEntry);
        Assert.All(seen.Steps, s => Assert.False(s.PinSeen));
    }

    [Fact]
    public async Task ADoubleTapIsOneConfirmTapAndOneExecution()
    {
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(Case("U1", null, Say(HammoudiSentence), Confirm(twice: true)), Token);

        Assert.Null(seen.StepError);
        Assert.Equal((1, 1), (seen.ConfirmTaps, seen.Steps[1].Executions));
        Assert.Single(seen.WalletDiff.NewTransactions);
    }

    [Fact]
    public async Task AWrongPinThenTheRightOneForTheTrader()
    {
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(
            Case("U3", null, Say(TraderSentence), Confirm("wrong"), Confirm("correct")), Token);

        Assert.Null(seen.StepError);
        Assert.True(Assert.Single(seen.Cards).View.PinRequired);
        var wrong = seen.Steps[1];
        Assert.Equal(("live", "pin_wrong", 2, 0L), (wrong.CardState, wrong.OutcomeCode, wrong.AttemptsLeft, wrong.MovedIqd));
        Assert.Equal(("done", 1), (seen.Steps[2].CardState, seen.Steps[2].Executions));
        Assert.Equal(2, seen.ConfirmTaps);
        Assert.Equal(1_100_000L, Assert.Single(seen.WalletDiff.NewTransactions).AmountIqd);
        Assert.All(seen.Steps, s => Assert.False(s.PinSeen));
    }

    [Fact]
    public async Task WaitingOutTheTimerExpiresTheCard()
    {
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(Case("U1", null, Say(HammoudiSentence), new CaseStep(Wait: "expire")), Token);

        Assert.Null(seen.StepError);
        Assert.Equal(("expired", "expired"), (seen.Steps[1].CardState, seen.Steps[1].OutcomeCode));
        Assert.Equal("expired", Assert.Single(seen.Cards).FinalState);
        Assert.Empty(seen.WalletDiff.NewTransactions);
        Assert.Equal(SeedFacts.DemoBalance, seen.WalletDiff.BalanceIqd);
        Assert.True(runner.Clock.Now > TimeProvider.System.GetUtcNow().AddSeconds(30));
    }

    [Fact]
    public async Task AWalletErrorOnConfirmIsNotDoneAndMovesNothing()
    {
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(
            Case("U1", new CaseFault("wallet_error", "confirm", 1), Say(HammoudiSentence), Confirm()), Token);

        // 500 injected_error: the wallet refused before writing (the 14 Oct ruling), so the card ends not done.
        Assert.Null(seen.StepError);
        Assert.Equal(["live", "not_done"], seen.StatusSequence);
        Assert.Equal(("not_done", 0), (seen.Steps[1].CardState, seen.Steps[1].Executions));
        Assert.Contains(ReasonCodes.NotDoneNothingTaken, seen.Steps[1].ReplyCodes);
        Assert.Empty(seen.WalletDiff.NewTransactions);
        Assert.Equal(SeedFacts.DemoBalance, seen.WalletDiff.BalanceIqd);
    }

    [Fact]
    public async Task AnUnreachableModelGivesModelUnavailable()
    {
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(
            Case("U1", new CaseFault("model_unreachable", "request"), Say(ModelSentence)), Token);

        Assert.Null(seen.StepError);
        Assert.Empty(seen.Cards);
        Assert.Equal([ReasonCodes.ModelUnavailable], seen.Steps[0].ReplyCodes);
        Assert.Equal([ModelSentence], seen.ParserTexts);
        Assert.Empty(runner.Parser.Usages);
    }

    [Fact]
    public async Task AnInvalidModelReplyIsNotUnderstoodAndGradedAsModelInvalidReply()
    {
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(
            Case("U1", new CaseFault("model_invalid_reply", "request"), Say(HammoudiSentence)), Token);

        Assert.Empty(seen.Cards);
        Assert.Equal([ReasonCodes.NotUnderstood], seen.Steps[0].ReplyCodes);
        // The runner grades the entry the conversation wrote: the injected invalid structure is model_invalid_reply.
        Assert.Equal(new LearningEntry(LearningLogRule.ModelInvalidReply, RecordingParser.InjectedProblem), seen.LearningEntry);
    }

    [Fact]
    public async Task AnExternalDebitWhileTheCardWaitsIsLeftOutOfTheCasesTransactions()
    {
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(
            Case("U1", new CaseFault("external_debit", "card_waiting", Amount: 5_000), Say(HammoudiSentence), Confirm()), Token);

        Assert.Null(seen.StepError);
        Assert.Equal("done", seen.Steps[1].CardState);
        var transaction = Assert.Single(seen.WalletDiff.NewTransactions);
        Assert.Equal(SeedFacts.HammoudiPhone, transaction.ToPhone);
        Assert.Equal(SeedFacts.DemoBalance - 5_000 - 51_000, seen.WalletDiff.BalanceIqd);
        Assert.Equal(51_000L, seen.Steps[1].MovedIqd);
    }

    [Fact]
    public async Task TryAgainIsMarkedUnsupported()
    {
        var runner = Start();

        var seen = await runner.Driver.PlayAsync(
            Case("U1", null, Say(HammoudiSentence), new CaseStep(Tap: "try_again"), Confirm()), Token);

        Assert.Equal("tap try_again", seen.UnsupportedStep);
        Assert.Single(seen.Steps);
        Assert.Empty(seen.WalletDiff.NewTransactions);
    }

    [Fact]
    public async Task EveryCaseStartsFromTheSeed()
    {
        var runner = Start();
        var transfer = Case("U1", null, Say(HammoudiSentence), Confirm());

        var first = await runner.Driver.PlayAsync(transfer, Token);
        var second = await runner.Driver.PlayAsync(transfer, Token);

        Assert.Equal(SeedFacts.DemoBalance, first.WalletBefore.BalanceIqd);
        Assert.Equal(SeedFacts.DemoBalance, second.WalletBefore.BalanceIqd);
        Assert.Equal(699_000, second.WalletDiff.BalanceIqd);
        Assert.Single(second.WalletDiff.NewTransactions);
        Assert.Equal([HammoudiSentence], second.ParserTexts);
        // The first case's card was closed; the second gets a fresh one, and no repeat warning from the first payment.
        Assert.Null(Assert.Single(second.Cards).View.Warning);
    }

    private sealed class Disposer(Action dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            dispose();
            return ValueTask.CompletedTask;
        }
    }
}
