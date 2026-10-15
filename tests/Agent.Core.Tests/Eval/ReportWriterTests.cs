using System.Text.Json;
using Agent.Eval.Grading;

namespace Agent.Core.Tests.Eval;

/// <summary>The report: the Wilson interval, the sections, and no case text (a synthetic run).</summary>
public sealed class ReportWriterTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("eval-report-");

    public void Dispose() => directory.Delete(recursive: true);

    [Theory]
    [InlineData(0, 10, 0.0, 27.8)]
    [InlineData(5, 10, 23.7, 76.3)]
    [InlineData(10, 10, 72.2, 100.0)]
    public void TheWilsonIntervalHasTheKnownValues(int passed, int total, double low, double high)
    {
        var (lo, hi) = ReportWriter.Wilson(passed, total);

        Assert.Equal(low, Math.Round(100 * lo, 1));
        Assert.Equal(high, Math.Round(100 * hi, 1));
    }

    [Fact]
    public void TheIntervalIsShownAsLowToHighPercent()
    {
        Assert.Equal("23.7–76.3 %", ReportWriter.Interval(ReportWriter.Score("x", [.. Results(5, 5)])));
    }

    [Fact]
    public void PercentilesUseTheNearestRank()
    {
        long[] times = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

        Assert.Equal((50L, 90L), (ReportWriter.Percentile(times, 50), ReportWriter.Percentile(times, 90)));
        Assert.Equal(0L, ReportWriter.Percentile([], 50));
    }

    [Fact]
    public void TheLabelFollowsTheParserInTheName()
    {
        var summary = Summary() with { Parser = "claude-haiku", Label = "m7a" };

        var paths = ReportWriter.Write(summary, directory.FullName);

        Assert.Equal("dev-claude-haiku-m7a-2026-10-14-0900.md", Path.GetFileName(paths.MarkdownPath));
        Assert.Equal("dev-claude-haiku-m7a-2026-10-14-0900.json", Path.GetFileName(paths.JsonPath));
    }

    [Fact]
    public void TheReportHasEverySectionAndNoCaseText()
    {
        var summary = Summary();

        var paths = ReportWriter.Write(summary, directory.FullName);

        Assert.Equal(("dev-scripted-2026-10-14-0900.md", "dev-scripted-2026-10-14-0900.json"), (Path.GetFileName(paths.MarkdownPath), Path.GetFileName(paths.JsonPath)));
        var markdown = File.ReadAllText(paths.MarkdownPath);
        var json = File.ReadAllText(paths.JsonPath);
        foreach (var heading in new[] { "## Totals", "## Per category", "## Per type", "## Red team", "## Masking", "## Time and cost", "## Cases", "## Unsupported steps" })
            Assert.Contains(heading, markdown, StringComparison.Ordinal);

        Assert.Contains("| RED-T1 | Red team | Transfer | no | cards[0]: expected transfer, observed card missing |", markdown, StringComparison.Ordinal);
        Assert.Contains("- RED-T1: 1 execution(s) without a tap", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("- RED-T2:", markdown, StringComparison.Ordinal);
        Assert.Contains("Red-team cases: 2; executions without a tap: 1 (not at the pass mark).", markdown, StringComparison.Ordinal);
        Assert.Contains("- MSK-T1: phone", markdown, StringComparison.Ordinal);
        Assert.Contains("- UNS-T1: tap save_alias", markdown, StringComparison.Ordinal);
        Assert.Contains("`" + summary.SetFileSha256 + "`", markdown, StringComparison.Ordinal);
        Assert.Contains("| Eval:SettleAttempts | 3 |", markdown, StringComparison.Ordinal);

        Assert.DoesNotContain(markdown, c => c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ﭐ' and <= '﻿');
        Assert.DoesNotContain(json, c => c is >= '؀' and <= 'ۿ');

        using var document = JsonDocument.Parse(json);
        Assert.Equal(7, document.RootElement.GetProperty("cases").GetArrayLength());
        var redTeam = document.RootElement.GetProperty("red_team");
        Assert.Equal(1, redTeam.GetProperty("executions_without_tap").GetInt32());
        Assert.False(redTeam.GetProperty("at_pass_mark").GetBoolean());
        Assert.Equal(1, redTeam.GetProperty("cases_with_executions_without_tap").GetArrayLength());
    }

    [Fact]
    public void ARunOfARepeatSeriesHasItsNumberAfterTheLabel()
    {
        var summary = Summary() with { Parser = "claude-haiku", Label = "v2", Run = 1 };

        Assert.Equal("dev-claude-haiku-v2-r1-2026-10-14-0900", ReportWriter.Stem(summary));
        Assert.Equal("dev-claude-haiku-r3-2026-10-14-0900", ReportWriter.Stem(summary with { Label = null, Run = 3 }));
        Assert.Equal("dev-claude-haiku-v2-r1-2026-10-14-0900.md", Path.GetFileName(ReportWriter.Write(summary, directory.FullName).MarkdownPath));
    }

    [Fact]
    public void TheMedianOfAnEvenCountIsTheMeanOfTheTwoMiddleValues()
    {
        Assert.Equal(2.5, ReportWriter.Median([4, 1, 3, 2]));
        Assert.Equal(2.0, ReportWriter.Median([3, 1, 2]));
        Assert.Equal(0.0, ReportWriter.Median([]));
    }

    [Fact]
    public void ARepeatSeriesGivesMinMedianMaxPerGroupAndTheFlippedCases()
    {
        var runs = RepeatRuns();

        var repeat = ReportWriter.Repeat(runs);

        Assert.Equal(
            [
                new RepeatGroup(ReportWriter.AllCases, 4, 2, 2.0, 3),
                new RepeatGroup("Clean", 2, 1, 2.0, 2),
                new RepeatGroup("Red team", 2, 0, 1.0, 1),
            ],
            repeat.Groups);
        Assert.Equal(["B-1", "C-1"], repeat.Flipped.Select(f => f.Id));
        Assert.Equal([true, false, true], repeat.Flipped[0].Passed);
        Assert.Equal([true, true, false], repeat.Flipped[1].Passed);
        Assert.Equal([3, 2, 2], repeat.Runs.Select(r => r.Passed));
        Assert.Equal([1, 2, 3], repeat.Runs.Select(r => r.Run));
        Assert.Equal(0.004m, repeat.Runs[0].CostUsd);
        Assert.Equal(25.0, repeat.Runs[0].MedianTimeMs);
    }

    [Fact]
    public void TheRepeatReportIsNamedAfterTheFirstRunAndHoldsNoCaseText()
    {
        var runs = RepeatRuns();
        var runPaths = runs.Select(r => ReportWriter.Write(r, directory.FullName)).ToList();

        var paths = ReportWriter.WriteRepeat(runs, runPaths, directory.FullName);

        Assert.Equal("dev-claude-haiku-v2-repeat3-2026-10-15-2100", ReportWriter.RepeatStem(runs));
        Assert.Equal("dev-claude-haiku-v2-repeat3-2026-10-15-2100.md", Path.GetFileName(paths.MarkdownPath));
        Assert.Equal("dev-claude-haiku-v2-repeat3-2026-10-15-2100.json", Path.GetFileName(paths.JsonPath));
        var markdown = File.ReadAllText(paths.MarkdownPath);
        Assert.Contains("| All cases | 4 | 2 | 2.0 | 3 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Red team | 2 | 0 | 1.0 | 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| B-1 | Clean | pass | fail | pass |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Model:CostUsd | 0.0120 |", markdown, StringComparison.Ordinal);
        Assert.Contains("- Run 2: 2/4 passed, cost 0.0040 USD, median time 25.0 ms", markdown, StringComparison.Ordinal);
        Assert.Contains("dev-claude-haiku-v2-r3-2026-10-15-2102.md", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("expected-text", markdown, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(File.ReadAllText(paths.JsonPath));
        Assert.Equal(3, json.RootElement.GetProperty("header").GetProperty("runs").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("flipped").GetArrayLength());
        Assert.Contains("2 / 2.0 / 3", ReportWriter.RepeatConsoleSummary(ReportWriter.Repeat(runs)), StringComparison.Ordinal);
    }

    /// <summary>Three runs of four cases (two Clean, two Red team): B-1 fails in run 2, C-1 in run 3, D-1 always.</summary>
    private static List<RunSummary> RepeatRuns()
    {
        bool[][] passes =
        [
            [true, true, true, false],
            [true, false, true, false],
            [true, true, false, false],
        ];
        string[] ids = ["A-1", "B-1", "C-1", "D-1"];
        string[] categories = ["Clean", "Clean", "Red team", "Red team"];
        long[] times = [10, 20, 30, 40];

        return [.. passes.Select((run, r) => new RunSummary(
            "dev",
            new string('b', 64),
            "claude-haiku",
            new DateTimeOffset(2026, 10, 15, 21, r, 0, TimeSpan.Zero),
            new Dictionary<string, string> { ["Model:PromptVersion"] = "2", ["Model:CostUsd"] = "0.0040" },
            [.. run.Select((passed, i) => new CaseResult(
                ids[i],
                categories[i],
                "Transfer",
                passed,
                passed ? [] : [new FieldVerdict("cards[0].fee", "1000", "expected-text", false)],
                true,
                true,
                times[i],
                0.001m,
                0,
                null))],
            "v2",
            r + 1))];
    }

    private static IEnumerable<CaseResult> Results(int passed, int failed)
    {
        for (var i = 0; i < passed; i++)
            yield return new CaseResult("P-" + i, "Clean", "Transfer", true, [], true, true, 100, 0m, 1, null);
        for (var i = 0; i < failed; i++)
            yield return new CaseResult("F-" + i, "Clean", "Transfer", false, [new FieldVerdict("cards[0].fee", "1000", "2000", false)], true, true, 100, 0m, 0, null);
    }

    private static RunSummary Summary()
    {
        CaseResult[] results =
        [
            new("CLN-T1", "Clean", "Transfer", true, [new FieldVerdict("cards[0].display", "display", "display equal", true)], true, true, 120, 0m, 1, null),
            new("CLN-T2", "Clean", "Bill", false, [new FieldVerdict("cards[0].fee", "0", "1000", false)], true, true, 80, 0m, 1, null),
            new("BAL-T1", "Clean", "Balance", true, [], true, true, 40, 0m, 0, null),
            new("RED-T1", "Red team", "Transfer", false, [new FieldVerdict("cards[0]", "transfer", "card missing", false)], true, true, 90, 0m, 1, null, ExecutionsWithoutTap: 1),
            new("RED-T2", "Red team", "Transfer", true, [], true, true, 90, 0m, 1, null, ExecutionsWithoutTap: 0),
            new("MSK-T1", "Malformed", "Transfer", false, [new FieldVerdict("outbound_text", "clean", "phone", false)], false, true, 70, 0m, 0, null),
            new("UNS-T1", "Ambiguous", "Transfer", false, [new FieldVerdict("unsupported_step", "none", "tap save_alias", false)], true, true, 60, 0m, 0, "tap save_alias"),
        ];

        return new RunSummary(
            "dev",
            new string('a', 64),
            "scripted",
            new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string> { ["Eval:SettleAttempts"] = "3", ["Agent:CardTimerSeconds"] = "45" },
            results);
    }
}
