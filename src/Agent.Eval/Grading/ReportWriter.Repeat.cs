using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Agent.Eval.Grading;

/// <summary>One group across the runs of a repeat series: its case count and the min, median and max of its passes.</summary>
public sealed record RepeatGroup(string Name, int Cases, int MinPassed, double MedianPassed, int MaxPassed);

/// <summary>A case that passed in some runs and failed in others, with its result in each run, in run order.</summary>
public sealed record FlippedCase(string Id, string Category, IReadOnlyList<bool> Passed);

/// <summary>One run of a repeat series: its passes, its cost and its median case time.</summary>
public sealed record RepeatRun(int Run, int Passed, int Cases, decimal CostUsd, double MedianTimeMs);

/// <summary>
/// A repeat series (<c>--repeat N</c>) summed up: the groups (all cases, then each category), the flipped cases, the runs,
/// and each target's min, median and max across the runs.
/// </summary>
public sealed record RepeatSummary(
    IReadOnlyList<RepeatGroup> Groups,
    IReadOnlyList<FlippedCase> Flipped,
    IReadOnlyList<RepeatRun> Runs,
    IReadOnlyList<RepeatTarget> Targets);

public static partial class ReportWriter
{
    /// <summary>The name of the group of every case in a repeat series.</summary>
    public const string AllCases = "All cases";

    /// <summary>
    /// The median of <paramref name="values"/>: the middle value, or the mean of the two middle values for an even count;
    /// 0 when empty.
    /// </summary>
    public static double Median(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
            return 0;
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>
    /// Sums up the runs of a repeat series, in run order. Groups are all cases, then each category in first-seen order of
    /// the first run; a flipped case is one that passed in some runs and failed in others (a case missing from a run
    /// counts as failed there).
    /// </summary>
    public static RepeatSummary Repeat(IReadOnlyList<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentOutOfRangeException.ThrowIfZero(runs.Count);

        var first = runs[0].Results;
        var groups = new List<RepeatGroup> { Group(AllCases, first.Count, runs.Select(r => r.Results.Count(c => c.Passed))) };
        foreach (var category in first.Select(r => r.Category).Distinct(StringComparer.Ordinal))
        {
            groups.Add(Group(
                category,
                first.Count(r => r.Category == category),
                runs.Select(run => run.Results.Count(c => c.Passed && c.Category == category))));
        }

        var flipped = new List<FlippedCase>();
        foreach (var result in first)
        {
            var marks = runs
                .Select(run => run.Results.FirstOrDefault(c => string.Equals(c.Id, result.Id, StringComparison.Ordinal))?.Passed ?? false)
                .ToList();
            if (marks.Distinct().Count() > 1)
                flipped.Add(new FlippedCase(result.Id, result.Category, marks));
        }

        var perRun = runs
            .Select((run, index) => new RepeatRun(
                run.Run ?? index + 1,
                run.Results.Count(c => c.Passed),
                run.Results.Count,
                run.Results.Sum(c => c.CostUsd),
                Median(run.Results.Select(c => (double)c.TimeMs))))
            .ToList();

        return new RepeatSummary(groups, flipped, perRun, RepeatTargets(runs));
    }

    /// <summary>
    /// Writes the aggregate report of a repeat series into <paramref name="directory"/>, named
    /// <c>&lt;set&gt;-&lt;parser&gt;[-&lt;label&gt;]-repeat&lt;N&gt;-&lt;UTC date&gt;-&lt;UTC HHmm&gt;</c> with the date and time of the
    /// first run (<see cref="RepeatStem"/>). Like every report it holds ids, categories, codes and figures, never a case's
    /// text (rule 7).
    /// </summary>
    public static ReportPaths WriteRepeat(IReadOnlyList<RunSummary> runs, IReadOnlyList<ReportPaths> runPaths, string directory)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(runPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);
        var stem = RepeatStem(runs);
        var summary = Repeat(runs);
        var settings = RepeatSettings(runs);
        var jsonPath = Path.Combine(directory, stem + ".json");
        var markdownPath = Path.Combine(directory, stem + ".md");

        var document = new
        {
            Header = new
            {
                Set = runs[0].SetName,
                SetFileSha256 = runs[0].SetFileSha256,
                runs[0].Parser,
                Runs = runs.Count,
                StartedUtc = runs[0].StartedUtc.UtcDateTime,
                Settings = settings,
            },
            summary.Groups,
            summary.Flipped,
            summary.Runs,
            summary.Targets,
            Reports = runPaths.Select(p => new { Markdown = Path.GetFileName(p.MarkdownPath), Json = Path.GetFileName(p.JsonPath) }).ToList(),
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(document, Json), Utf8NoBom);
        File.WriteAllText(markdownPath, RepeatMarkdown(runs, runPaths), Utf8NoBom);
        return new ReportPaths(jsonPath, markdownPath);
    }

    /// <summary>The file name, without extension, of the aggregate report of a repeat series (see <see cref="WriteRepeat"/>).</summary>
    public static string RepeatStem(IReadOnlyList<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentOutOfRangeException.ThrowIfZero(runs.Count);
        var first = runs[0];
        return first.SetName + "-" + first.Parser + "-"
            + (string.IsNullOrEmpty(first.Label) ? string.Empty : first.Label + "-")
            + "repeat" + runs.Count.ToString(CultureInfo.InvariantCulture) + "-"
            + StampOf(first.StartedUtc);
    }

    /// <summary>The Markdown aggregate report of a repeat series.</summary>
    public static string RepeatMarkdown(IReadOnlyList<RunSummary> runs, IReadOnlyList<ReportPaths> runPaths)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(runPaths);
        var summary = Repeat(runs);
        var first = runs[0];
        var md = new StringBuilder();
        void Line(string text = "") => md.Append(text).Append('\n');

        Line("# Eval repeat report: " + first.SetName + " set, " + Number(runs.Count) + " runs");
        Line();
        Line("| Item | Value |");
        Line("|---|---|");
        Line("| Set | " + first.SetName + " |");
        Line("| Case file SHA-256 | `" + first.SetFileSha256 + "` |");
        Line("| Parser | " + first.Parser + " |");
        Line("| Runs | " + Number(runs.Count) + " |");
        Line("| First run started (UTC) | " + first.StartedUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " |");
        foreach (var (key, value) in RepeatSettings(runs))
            Line("| " + key + " | " + value + " |");
        Line();

        Line("## Passed across the runs");
        Line();
        Line("| Group | Cases | Min | Median | Max |");
        Line("|---|---|---|---|---|");
        foreach (var g in summary.Groups)
            Line("| " + g.Name + " | " + Number(g.Cases) + " | " + Number(g.MinPassed) + " | " + OneDecimal(g.MedianPassed) + " | " + Number(g.MaxPassed) + " |");
        Line();

        Line("## Targets");
        Line();
        RepeatTargetsTable(md, summary.Targets);
        Line();

        Line("## Flipped cases");
        Line();
        Line("Cases that passed in some runs and failed in others: " + Number(summary.Flipped.Count) + ".");
        Line();
        if (summary.Flipped.Count > 0)
        {
            var header = string.Concat(summary.Runs.Select(r => " r" + Number(r.Run) + " |"));
            Line("| Id | Category |" + header);
            Line("|---|---|" + string.Concat(summary.Runs.Select(_ => "---|")));
            foreach (var f in summary.Flipped)
                Line("| " + f.Id + " | " + f.Category + " |" + string.Concat(f.Passed.Select(p => (p ? " pass" : " fail") + " |")));
            Line();
        }

        Line("## Runs");
        Line();
        foreach (var r in summary.Runs)
        {
            Line("- Run " + Number(r.Run) + ": " + Number(r.Passed) + "/" + Number(r.Cases) + " passed, cost "
                + r.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture) + " USD, median time " + OneDecimal(r.MedianTimeMs) + " ms");
        }

        Line();
        Line("## Run reports");
        Line();
        foreach (var p in runPaths)
            Line("- " + Path.GetFileName(p.MarkdownPath) + ", " + Path.GetFileName(p.JsonPath));

        return md.ToString();
    }

    /// <summary>The console summary of a repeat series: each group's min, median and max, the flipped cases, each run, and the targets.</summary>
    public static string RepeatConsoleSummary(RepeatSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append(Environment.NewLine);

        Line("Repeat of " + Number(summary.Runs.Count) + " runs (passed: min / median / max):");
        foreach (var g in summary.Groups)
            Line("  " + g.Name + " (" + Number(g.Cases) + "): " + Number(g.MinPassed) + " / " + OneDecimal(g.MedianPassed) + " / " + Number(g.MaxPassed));
        Line("Flipped cases: " + Number(summary.Flipped.Count)
            + (summary.Flipped.Count == 0 ? string.Empty : " (" + string.Join(", ", summary.Flipped.Select(f => f.Id + " " + string.Concat(f.Passed.Select(p => p ? "P" : "F")))) + ")"));
        foreach (var r in summary.Runs)
        {
            Line("  Run " + Number(r.Run) + ": " + Number(r.Passed) + "/" + Number(r.Cases) + " passed, "
                + r.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture) + " USD, median " + OneDecimal(r.MedianTimeMs) + " ms");
        }

        Line("Targets (min / median / max, verdict on the median):");
        foreach (var t in summary.Targets)
            Line("  " + RepeatTargetLine(t));

        return text.ToString();
    }

    /// <summary>The first run's header settings, with <c>Model:CostUsd</c> summed over the runs, ordered by key.</summary>
    private static SortedDictionary<string, string> RepeatSettings(IReadOnlyList<RunSummary> runs)
    {
        var settings = new SortedDictionary<string, string>(runs[0].Settings.ToDictionary(), StringComparer.Ordinal)
        {
            ["Model:CostUsd"] = runs.Sum(r => r.Results.Sum(c => c.CostUsd)).ToString("0.0000", CultureInfo.InvariantCulture),
        };
        return settings;
    }

    private static RepeatGroup Group(string name, int cases, IEnumerable<int> passes)
    {
        var list = passes.ToList();
        return new RepeatGroup(name, cases, list.Min(), Median(list.Select(p => (double)p)), list.Max());
    }

    private static string OneDecimal(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
