using Agent.Core.Reports;
using Agent.Eval.Grading;
using Agent.Eval.Running;

namespace Agent.Core.Tests.Reports;

/// <summary>
/// The console's reader of the committed reports (milestone 10): every report with a targets table reads, the listing is
/// newest first with an aggregate before its first run, and the default is the candidate's newest report.
/// </summary>
public sealed class ReportReaderTests : IDisposable
{
    private const string NoTargets = "dev-scripted-oracle-2026-10-15-2052.json";
    private const string SonnetRun3 = "dev-claude-sonnet-r3-2026-10-29-1058.json";
    private const string SonnetRepeat = "dev-claude-sonnet-repeat3-2026-10-29-1049.json";
    private const string SonnetRun1 = "dev-claude-sonnet-r1-2026-10-29-1049.json";
    private const string GemmaRun3 = "dev-compatible-gemma-r3-2026-11-01-0735.json";

    private static readonly string[] Verdicts = ["pass", "miss", "reported", "n/a"];

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("report-reader-");

    private static string Committed => Path.Combine(EvalPaths.RepositoryRoot(), "docs", "reports");

    public void Dispose() => directory.Delete(recursive: true);

    private string CopyIn(params string[] names)
    {
        foreach (var name in names)
            File.Copy(Path.Combine(Committed, name), Path.Combine(directory.FullName, name));
        return directory.FullName;
    }

    [Fact]
    public void EveryCommittedReportWithTargetsReads()
    {
        var files = Directory.GetFiles(Committed, "dev-*.json").Concat(Directory.GetFiles(Committed, "heldout-*.json"));
        var read = 0;
        foreach (var file in files)
        {
            var report = ReportReader.Read(file);
            var targets = report.Single?.Targets?.Select(t => t.Verdict) ?? report.Repeat?.Targets?.Select(t => t.Verdict);
            if (targets is null || !targets.Any())
                continue;

            read++;
            Assert.Contains(report.Header.Set, new[] { "dev", "heldout" });
            Assert.Equal(9, targets.Count());
            Assert.All(targets, v => Assert.Contains(v, Verdicts));
        }

        Assert.True(read >= 100, read + " reports read");
    }

    [Fact]
    public void ListSkipsFilesWithoutTargets()
    {
        var listing = ReportReader.List(CopyIn(NoTargets, SonnetRun3), "sonnet");

        Assert.Equal((1, 1), (listing.Entries.Count, listing.Skipped));
        Assert.Equal(SonnetRun3, listing.Entries[0].FileName);
    }

    [Fact]
    public void ListSkipsUnparseableFile()
    {
        File.WriteAllText(Path.Combine(directory.FullName, "dev-bad.json"), "{");

        var listing = ReportReader.List(directory.FullName, "sonnet");

        Assert.Empty(listing.Entries);
        Assert.Equal(1, listing.Skipped);
    }

    [Fact]
    public void ListOfMissingDirectoryIsEmpty()
    {
        var listing = ReportReader.List(Path.Combine(directory.FullName, "absent"), "sonnet");

        Assert.Empty(listing.Entries);
        Assert.Equal(0, listing.Skipped);
        Assert.Null(listing.DefaultFileName);
    }

    [Fact]
    public void AggregateComesBeforeItsFirstRunOnEqualStart()
    {
        var listing = ReportReader.List(CopyIn(SonnetRun1, SonnetRepeat), "sonnet");

        Assert.Equal([SonnetRepeat, SonnetRun1], listing.Entries.Select(e => e.FileName));
        Assert.True(listing.Entries[0].IsAggregate);
        Assert.False(listing.Entries[1].IsAggregate);
    }

    [Fact]
    public void DefaultIsTheNewestReportOfTheCandidate()
    {
        var listing = ReportReader.List(CopyIn(SonnetRun1, SonnetRepeat, GemmaRun3), "sonnet");

        Assert.Equal(GemmaRun3, listing.Entries[0].FileName);
        Assert.Equal(SonnetRepeat, listing.DefaultFileName);
    }

    [Fact]
    public void DefaultPrefersTheCandidatesNewestAggregate()
    {
        // The aggregate carries its first run's start (10:49), so its own later run (10:58) lists first.
        var listing = ReportReader.List(CopyIn(SonnetRun3, SonnetRepeat), "sonnet");

        Assert.Equal(SonnetRun3, listing.Entries[0].FileName);
        Assert.Equal(SonnetRepeat, listing.DefaultFileName);
    }

    [Fact]
    public void ResolveGivesARootedFullPath()
    {
        var resolved = ReportReader.Resolve("docs/reports");

        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal(Path.GetFullPath(resolved), resolved);
        if (OperatingSystem.IsWindows())
            Assert.DoesNotContain('/', resolved);
    }

    [Fact]
    public void DefaultFallsBackToNewestWhenNoParserMatches()
    {
        var listing = ReportReader.List(CopyIn(SonnetRun1, SonnetRepeat, GemmaRun3), "opus");

        Assert.Equal(listing.Entries[0].FileName, listing.DefaultFileName);
    }

    [Fact]
    public void ReadOfASingleRunGivesTotalsTargetsTimeAndCost()
    {
        var report = ReportReader.Read(Path.Combine(Committed, SonnetRun3));

        Assert.Null(report.Repeat);
        var single = Assert.IsType<ReportDocument>(report.Single);
        Assert.Equal((102, 97), (single.Totals.Cases, single.Totals.Passed));
        Assert.Equal(9, single.Targets?.Count);
        Assert.Equal(2376, single.Time.MedianMs);
        Assert.Equal(0.1769595m, single.CostUsd);
        Assert.Equal("claude-sonnet", report.Header.Parser);
        Assert.Null(report.Header.Runs);
    }

    [Fact]
    public void ReadOfAnAggregateGivesGroupsRunsAndTargets()
    {
        var report = ReportReader.Read(Path.Combine(Committed, SonnetRepeat));

        Assert.Null(report.Single);
        var repeat = Assert.IsType<RepeatReportDocument>(report.Repeat);
        Assert.Equal(3, repeat.Runs.Count);
        Assert.Equal(ReportWriter.AllCases, repeat.Groups[0].Name);
        Assert.Equal(9, repeat.Targets?.Count);
        Assert.Equal(3, report.Header.Runs);
    }

    [Fact]
    public void TheWriterAndTheReaderAgreeOnTheTargets()
    {
        CaseResult[] results =
        [
            new("CLN-1", "Clean", "Transfer", true, [], true, true, 100, 0.002m, 1, null, CardsIssued: 1, TimeToFirstCardMs: 1_000),
            new("CLN-2", "Clean", "Transfer", false, [new FieldVerdict("cards[0].display", "a", "b", false)], true, true, 300, 0.004m, 1, null,
                CardsIssued: 1, TimeToFirstCardMs: 2_000),
            new("AMB-1", "Ambiguous", "Transfer", true, [new FieldVerdict("asks.count", "1", "1", true)], true, true, 200, 0m, 0, null,
                AsksExpected: 1, AsksObserved: 1, CardsIssued: 1, TimeToFirstCardMs: 5_000),
            new("RED-1", "Red team", "Transfer", true, [], true, true, 150, 0m, 0, null),
        ];
        var summary = new RunSummary(
            "dev",
            new string('a', 64),
            "claude-sonnet",
            new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string> { ["Model:PromptVersion"] = "6" },
            results);

        var paths = ReportWriter.Write(summary, directory.FullName);
        var report = ReportReader.Read(paths.JsonPath);

        var single = Assert.IsType<ReportDocument>(report.Single);
        Assert.Equal(ReportWriter.Targets(results), single.Targets!);
        Assert.Equal(ReportWriter.Score(ReportWriter.AllCases, results), single.Totals);
        Assert.Equal(new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc), report.Header.StartedUtc);
    }
}
