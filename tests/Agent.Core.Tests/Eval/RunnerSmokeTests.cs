using Agent.Core.Parsing;
using Agent.Core.Pipeline;
using Agent.Core.Requests;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Wallet;
using Agent.Eval.Cases;
using Agent.Eval.Grading;
using Agent.Eval.Running;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Core.Tests.Eval;

/// <summary>
/// Three synthetic English cases end to end through <see cref="CaseDriver"/> and <see cref="CaseGrader"/> against the
/// agent host's in-process wallet: a clean transfer with a tap, a balance question, and a name that matches no contact.
/// The keys are written here from the seed (runner fixtures, not answer keys).
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class RunnerSmokeTests(AgentHost host) : IAsyncDisposable
{
    private const string TransferSentence = "send fifty thousand to my brother Hammoudi";
    private const string BalanceSentence = "how much do I have";
    private const string UnknownSentence = "send twenty thousand to Zorro";

    private readonly List<IAsyncDisposable> disposables = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        for (var i = disposables.Count - 1; i >= 0; i--)
            await disposables[i].DisposeAsync();
    }

    private (CaseDriver Driver, CaseGrader Grader) Start()
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
            new ScriptedSentence(TransferSentence, ParsedRequest.Transfer(SeedFacts.Contact("S01").Alias, 50, AmountUnit.Thousand)),
            new ScriptedSentence(BalanceSentence, ParsedRequest.Balance()),
            new ScriptedSentence(UnknownSentence, ParsedRequest.Transfer("Zorro", 20, AmountUnit.Thousand)),
        ]));
        var driver = new CaseDriver(provider, admin, pins, provider.GetRequiredService<EvalClock>(), parser, new EvalOptions { SettleDelayMs = 50 });
        return (driver, new CaseGrader(pins));
    }

    private static EvalCase Case(string id, string type, Expectation expect, params CaseStep[] steps)
        => new(id, "dev", "Clean", type, "U1", steps[0].Say!, steps, null, expect);

    private static IEnumerable<EvalCase> Cases()
    {
        var hammoudi = SeedFacts.Contact("S01");
        yield return Case(
            "SMOKE-01",
            "Transfer",
            new Expectation(
                Asks: [],
                Cards:
                [
                    new ExpectedCard(
                        "transfer", To: "S01", ToNumber: SeedFacts.HammoudiPhone, Display: hammoudi.Alias,
                        RegisteredName: SeedFacts.Account(SeedFacts.HammoudiPhone).RegisteredName,
                        Amount: 50_000, Fee: 1_000, Total: 51_000, BalanceAfter: 699_000, PinRequired: false, FirstTime: false,
                        Warning: null, Outcome: "done"),
                ],
                Replies: [],
                ConsentScreen: false,
                Final: new ExpectedFinal(699_000, [new ExpectedTransaction("transfer", 50_000, 1_000, SeedFacts.HammoudiPhone, "S01")]),
                LearningLog: null),
            new CaseStep(Say: TransferSentence),
            new CaseStep(Tap: "confirm", Expect: new StepExpect(CardOutcome: "done", Executions: 1)));

        yield return Case(
            "SMOKE-02",
            "Balance",
            new Expectation(
                Asks: [],
                Cards: [],
                Replies: [new ExpectedReply(ReasonCodes.Balance, SeedFacts.DemoBalance)],
                Final: new ExpectedFinal(SeedFacts.DemoBalance, []),
                LearningLog: null),
            new CaseStep(Say: BalanceSentence));

        yield return Case(
            "SMOKE-03",
            "Transfer",
            new Expectation(
                Asks: [],
                Cards: [],
                Refusal: new ExpectedRefusal(ReasonCodes.RecipientNotFound),
                Final: new ExpectedFinal(SeedFacts.DemoBalance, []),
                LearningLog: new ExpectedLearning(ReasonCodes.RecipientNotFound)),
            new CaseStep(Say: UnknownSentence));
    }

    [Fact]
    public async Task ThreeCasesRunEndToEndAndAreGraded()
    {
        var (driver, grader) = Start();

        var results = new List<CaseResult>();
        foreach (var evalCase in Cases())
            results.Add(grader.Grade(evalCase, await driver.PlayAsync(evalCase, Token)));

        var transfer = results[0];
        var balance = results[1];
        var unknown = results[2];
        Assert.True(transfer.Passed, Describe(transfer));
        Assert.True(balance.Passed, Describe(balance));
        Assert.Equal((1, 0), (transfer.Executions, balance.Executions));
        Assert.All(results, r => Assert.Equal(0, r.ExecutionsWithoutTap));

        Assert.Contains(unknown.Verdicts, v => v is { Field: "refusal.reason", Expected: "recipient_not_found", Observed: "recipient_not_found", Ok: true });
        Assert.Contains(unknown.Verdicts, v => v is { Field: "refusal.no_card", Ok: true });
        Assert.True(unknown.LearningOk);
        Assert.True(unknown.Passed, Describe(unknown));
        Assert.Equal(0, unknown.Executions);
        Assert.All(results, r => Assert.True(r.OutboundOk));
    }

    [Fact]
    public async Task AWrongKeyIsReportedNotHidden()
    {
        var (driver, grader) = Start();
        var transfer = Cases().First();
        var wrongFee = transfer with
        {
            Expect = transfer.Expect with { Cards = [transfer.Expect.Cards![0] with { Fee = 2_000, Total = 52_000, BalanceAfter = 698_000 }] },
        };

        var result = grader.Grade(wrongFee, await driver.PlayAsync(wrongFee, Token));

        Assert.False(result.Passed);
        Assert.Equal("cards[0].fee", result.FirstFailing?.Field);
    }

    private sealed class Disposer(Action dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static string Describe(CaseResult result)
        => result.Id + ": " + (result.FirstFailing is { } f ? f.Field + " expected " + f.Expected + " observed " + f.Observed : "checks");
}
