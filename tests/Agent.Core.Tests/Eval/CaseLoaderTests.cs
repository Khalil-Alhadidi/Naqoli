using System.Text.Json;
using Agent.Core.Tests.Wallet;
using Agent.Eval.Cases;
using Agent.Eval.Running;

namespace Agent.Core.Tests.Eval;

/// <summary>
/// The case loader on the runner's own synthetic fixture (<c>cases/runner-fixture.jsonl</c>: English placeholders,
/// runner fixtures and not answer keys), on broken copies of it, and on the development set's shape. No case text is
/// printed.
/// </summary>
public sealed class CaseLoaderTests : IDisposable
{
    private static readonly string Fixture = Path.Combine(
        SeedHashTests.RepositoryRoot(), "tests", "Agent.Core.Tests", "Eval", "cases", "runner-fixture.jsonl");

    private readonly string directory = Directory.CreateTempSubdirectory("eval-cases-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string Write(params string[] lines)
    {
        var path = Path.Combine(directory, "cases.jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string FixtureLine(int index) => File.ReadAllLines(Fixture)[index];

    [Fact]
    public void TheFixtureLoadsWithEveryStepForm()
    {
        var cases = CaseLoader.Load(Fixture);

        Assert.Equal(["FIX-01", "FIX-02", "FIX-03"], cases.Select(c => c.Id));
        var steps = cases[0].Steps;
        Assert.Equal("say", CaseDriver.KindOf(steps[0]));
        Assert.Equal("S01", steps[1].ValueText);
        Assert.Equal("50000", steps[2].ValueText);
        Assert.Equal(("confirm", "wrong"), (steps[4].Tap, steps[4].Pin));
        Assert.True(steps[5].Twice);
        Assert.Equal("correct", steps[5].Pin);
        Assert.Equal(["cancel", "try_again", "save_alias"], steps.Skip(6).Take(3).Select(s => s.Tap));
        Assert.Equal(["expire", "expire_if_card"], steps.Skip(9).Select(s => s.Wait));
        Assert.Equal(new CaseFault("wallet_error", "confirm", 1), cases[0].Fault);
    }

    [Fact]
    public void TheFixtureLoadsEveryStepExpectation()
    {
        var steps = CaseLoader.Load(Fixture)[0].Steps;

        var yes = steps[3].Expect!;
        Assert.Equal(("unchanged", "points_to_confirm", 0L), (yes.Card, yes.Reply, yes.Moved!.Value));
        Assert.Equal((2, false), (steps[4].Expect!.PinAttemptsLeft!.Value, steps[4].Expect!.PinInChatOrLogs!.Value));
        Assert.Equal(1, steps[5].Expect!.Executions);
        Assert.Equal("done", steps[5].Expect!.CardOutcome);
        Assert.Equal(["checking", "done"], steps[5].Expect!.StatusSequence!);
        var cancel = steps[6].Expect!;
        Assert.Equal(("max_three_requests", false), (cancel.ThenReply, cancel.NewRequest!.Value));
        Assert.Equal(new Dictionary<int, bool> { [2] = true }, cancel.NoCard);
        Assert.Null(cancel.Extra);
        Assert.Equal("Hammoudi", steps[8].Expect!.AliasOffer);
    }

    [Fact]
    public void TheFixtureLoadsEveryAnswerKeyField()
    {
        var cases = CaseLoader.Load(Fixture);
        var first = cases[0].Expect;
        var second = cases[1].Expect;
        var third = cases[2].Expect;

        Assert.Equal(["S01", "S02"], first.Asks![0].OptionTexts!);
        Assert.Equal(["50000", "500000"], first.Asks[1].OptionTexts!);
        Assert.Equal("typed", first.Asks[2].Answer);
        Assert.Equal(
            new ExpectedCard(
                "transfer", "S01", "07805550101", "Hammoudi", "Hammoudi Placeholder", Amount: 50000, Fee: 1000, Total: 51000,
                BalanceAfter: 699000, PinRequired: false, FirstTime: false, Warning: null, Outcome: "done"),
            first.Cards![0]);
        Assert.Equal(
            [new ExpectedReply("balance", 750000), new ExpectedReply("max_three_requests", NotPrepared: "the fourth part")],
            first.Replies!);
        Assert.Equal(new ExpectedTransaction("transfer", 50000, 1000, "07805550101", "S01"), first.Final!.Transactions![0]);
        Assert.False(first.ConsentScreen);
        Assert.Null(first.Refusal);
        Assert.Null(first.LearningLog);

        Assert.Equal(("bill", "BS9", "water"), (second.Cards![0].Kind, second.Cards[0].Bill, second.Cards[0].Biller));
        Assert.Null(second.Cards[0].Label);
        Assert.Equal("KR-CR-5", second.Cards[1].Product);
        var refusal = second.Refusal!;
        Assert.Equal(
            ("over_per_transaction_cap", 5000000L, 3000000L, "water", "withdrawal", false),
            (refusal.Reason, refusal.Limit!.Value, refusal.LeftToday!.Value, refusal.Biller, refusal.What, refusal.NumberInReply!.Value));
        Assert.Equal(["KR-CR-5", "KR-CR-10"], refusal.Available!);
        Assert.True(second.ConsentScreen);
        Assert.Equal((13500L, 1, 0L), (second.Final!.Balance, second.Final.CodesIssued, second.Final.BillsNow!["BS9"]));
        Assert.Equal(new ExpectedTransaction("purchase", 5000, 0, Product: "KR-CR-5"), second.Final.Transactions![1]);
        Assert.Equal(new ExpectedLearning("bill_not_found", "water"), second.LearningLog);
        Assert.Equal(new CaseFault("external_debit", "card_waiting", Amount: 5000), cases[1].Fault);
        Assert.True(cases[1].PendingWordingRecheck);
        Assert.Equal("FIX-01", cases[1].Parent);

        var either = third.Either!;
        Assert.Equal(2, either.Count);
        Assert.Equal("expired", either[0].Cards![0].Outcome);
        Assert.Null(either[0].Reply);
        Assert.Equal(new ExpectedAlternativeReply("not_supported"), either[1].Reply);
        Assert.Equal(new ExpectedLearning("other_language"), either[1].LearningLog);
        Assert.Equal(new CaseFault("model_unreachable", "request"), cases[2].Fault);
    }

    [Fact]
    public void AMalformedLineNamesItsLineAndCase()
    {
        var path = Write(FixtureLine(0), string.Empty, FixtureLine(1)[..^5]);

        var ex = Assert.Throws<CaseFileException>(() => CaseLoader.Load(path));

        Assert.Equal(3, ex.Line);
        Assert.Contains("line 3", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("water bill", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownKeyIsRefused()
    {
        var path = Write(FixtureLine(0).Replace("\"consent_screen\": false", "\"consent_screen\": false, \"surprise\": 1", StringComparison.Ordinal));

        var ex = Assert.Throws<CaseFileException>(() => CaseLoader.Load(path));

        Assert.Equal((1, "FIX-01"), (ex.Line, ex.CaseId));
        Assert.Contains("surprise", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownStepExpectKeyIsRefused()
    {
        var path = Write(FixtureLine(0).Replace("\"executions\": 1", "\"executions\": 1, \"exections\": 1", StringComparison.Ordinal));

        var ex = Assert.Throws<CaseFileException>(() => CaseLoader.Load(path));

        Assert.Contains("exections", ex.Message, StringComparison.Ordinal);
        Assert.Equal("FIX-01", ex.CaseId);
    }

    [Theory]
    [InlineData("{\"say\": \"yes\",", "{\"say\": \"yes\", \"tap\": \"confirm\",")]
    [InlineData("{\"tap\": \"cancel\",", "{\"tap\": \"jump\",")]
    [InlineData("{\"wait\": \"expire\",", "{\"wait\": \"forever\",")]
    [InlineData("\"code\": \"wallet_error\"", "\"code\": \"wallet_melted\"")]
    [InlineData("\"on\": \"confirm\"", "\"on\": \"lunch\"")]
    [InlineData("\"pin\": \"wrong\"", "\"pin\": \"1234\"")]
    [InlineData("\"set\": \"dev\", ", "")]
    [InlineData("\"value\": \"S01\"", "\"value\": [1]")]
    public void AStepFormOrFaultOutsideTheFormatIsRefused(string from, string to)
    {
        var line = FixtureLine(0);
        Assert.Contains(from, line, StringComparison.Ordinal);
        var path = Write(line.Replace(from, to, StringComparison.Ordinal));

        Assert.Equal(1, Assert.Throws<CaseFileException>(() => CaseLoader.Load(path)).Line);
    }

    [Fact]
    public void TwoCasesWithOneIdAreRefused()
    {
        var path = Write(FixtureLine(0), FixtureLine(0));

        Assert.Equal(2, Assert.Throws<CaseFileException>(() => CaseLoader.Load(path)).Line);
    }

    [Fact]
    public void AMissingFileIsACaseFileProblem()
    {
        Assert.Throws<CaseFileException>(() => CaseLoader.Load(Path.Combine(directory, "none.jsonl")));
    }

    /// <summary>The development set's shape fits the model (counts only; no case is read out).</summary>
    [Fact]
    public void TheDevelopmentSetLoads()
    {
        var cases = CaseLoader.Load(Path.Combine(SeedHashTests.RepositoryRoot(), "data", "requests.dev.jsonl"));

        Assert.Equal(75, cases.Count);
        Assert.All(cases, c => Assert.Equal("dev", c.Set));
        Assert.All(cases, c => Assert.NotNull(c.Expect.Final));
    }

    [Fact]
    public void AValueIsAStringOrANumber()
    {
        using var text = JsonDocument.Parse("\"store:internet\"");
        using var number = JsonDocument.Parse("25000");
        using var list = JsonDocument.Parse("[1]");

        Assert.Equal("store:internet", CaseValues.Text(text.RootElement));
        Assert.Equal("25000", CaseValues.Text(number.RootElement));
        Assert.Null(CaseValues.Text(null));
        Assert.Throws<JsonException>(() => CaseValues.Text(list.RootElement));
    }
}
