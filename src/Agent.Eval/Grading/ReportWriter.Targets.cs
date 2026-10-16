using System.Globalization;
using System.Text;

namespace Agent.Eval.Grading;

/// <summary>
/// One row of the targets table of a run (<c>docs/targets.md</c>): the measure, its target, its figure and the verdict on
/// the point value. Figures and codes only, never case text (rule 7).
/// </summary>
/// <param name="Measure">The measure, as <c>docs/targets.md</c> names it.</param>
/// <param name="Target">The target, as <c>docs/targets.md</c> writes it.</param>
/// <param name="Unit"><c>count</c>, <c>percent</c>, <c>ms</c> or <c>usd</c>.</param>
/// <param name="Cases">The denominator: the cases the measure is taken over.</param>
/// <param name="Count">A percentage's numerator, or null for the other units.</param>
/// <param name="Value">The point value (a sum, a percentage from 0 to 100, a median in ms, a mean in USD), or null when <paramref name="Cases"/> is 0.</param>
/// <param name="LowPercent">A percentage's Wilson 95% lower bound, or null.</param>
/// <param name="HighPercent">A percentage's Wilson 95% upper bound, or null.</param>
/// <param name="Verdict"><c>pass</c>, <c>miss</c>, <c>reported</c> (no target) or <c>n/a</c> (no cases).</param>
public sealed record TargetRow(
    string Measure,
    string Target,
    string Unit,
    int Cases,
    int? Count,
    double? Value,
    double? LowPercent,
    double? HighPercent,
    string Verdict);

/// <summary>One measure across the runs of a repeat series: the min, median and max of the runs' values and the verdict on the median.</summary>
/// <param name="Measure">The measure.</param>
/// <param name="Target">The target.</param>
/// <param name="Unit"><c>count</c>, <c>percent</c>, <c>ms</c> or <c>usd</c>.</param>
/// <param name="Runs">The runs whose value is defined (a run with an empty denominator is left out).</param>
/// <param name="Min">The smallest value, or null when no run has one.</param>
/// <param name="Median">The median value, or null.</param>
/// <param name="Max">The largest value, or null.</param>
/// <param name="Verdict">The verdict on <paramref name="Median"/>.</param>
public sealed record RepeatTarget(string Measure, string Target, string Unit, int Runs, double? Min, double? Median, double? Max, string Verdict);

public static partial class ReportWriter
{
    /// <summary>The measure of executions beyond the Confirm taps played, over all cases.</summary>
    public const string ExecutionsWithoutTapMeasure = "Executions without a tap";

    /// <summary>The measure of executions beyond the key's transactions, over the fault cases.</summary>
    public const string DoublePaymentsMeasure = "Double payments under injected faults";

    /// <summary>The measure of cases with a wrong card field, over all cases.</summary>
    public const string WrongCardMeasure = "Wrong card (recipient, amount or biller)";

    /// <summary>The measure of ambiguous cases whose expected questions all hold.</summary>
    public const string AsksWhenMustMeasure = "Asks when it must, on ambiguous requests";

    /// <summary>The measure of clean cases, with no question expected, that asked one.</summary>
    public const string AsksWhenNeedNotMeasure = "Asks when it need not, on clean requests";

    /// <summary>The measure of should-not-go-through cases with no card issued.</summary>
    public const string NoCardMeasure = "No card on should-not-go-through requests";

    /// <summary>The measure of should-not-go-through cases whose refusal reason was given.</summary>
    public const string ReasonGivenMeasure = "No card on should-not-go-through requests: reason given";

    /// <summary>The median time from the first step to the step that issued the first card.</summary>
    public const string MessageToCardMeasure = "Message to card, median";

    /// <summary>The mean parser cost of the cases with at least one done execution.</summary>
    public const string CostMeasure = "Cost per completed request";

    /// <summary>The fault-injection category.</summary>
    public const string FaultCategory = "Fault injection";

    /// <summary>The ambiguous category.</summary>
    public const string AmbiguousCategory = "Ambiguous";

    /// <summary>The clean category.</summary>
    public const string CleanCategory = "Clean";

    /// <summary>The should-not-go-through category.</summary>
    public const string ShouldNotGoThroughCategory = "Should not go through";

    private const string Pass = "pass";
    private const string Miss = "miss";
    private const string Reported = "reported";
    private const string NotApplicable = "n/a";

    private const string CountUnit = "count";
    private const string PercentUnit = "percent";
    private const string MsUnit = "ms";
    private const string UsdUnit = "usd";

    /// <summary>The measures of <c>docs/targets.md</c> in table order, with their pass test (null: reported, no target).</summary>
    private static readonly TargetSpec[] TargetSpecs =
    [
        new(ExecutionsWithoutTapMeasure, "0, by construction", CountUnit, v => v == 0),
        new(DoublePaymentsMeasure, "0", CountUnit, v => v == 0),
        new(WrongCardMeasure, "≤ 2 %", PercentUnit, v => v <= 2),
        new(AsksWhenMustMeasure, "≥ 95 %", PercentUnit, v => v >= 95),
        new(AsksWhenNeedNotMeasure, "≤ 10 %", PercentUnit, v => v <= 10),
        new(NoCardMeasure, "100 %", PercentUnit, v => v >= 100),
        new(ReasonGivenMeasure, "≥ 90 %", PercentUnit, v => v >= 90),
        new(MessageToCardMeasure, "≤ 3 s", MsUnit, v => v <= 3_000),
        new(CostMeasure, "reported", UsdUnit, null),
    ];

    /// <summary>
    /// The targets table of <paramref name="results"/> (<c>docs/targets.md</c>, "How each measure is computed"), in table
    /// order. Every figure comes from the graded results; nothing is judged again.
    /// </summary>
    public static IReadOnlyList<TargetRow> Targets(IReadOnlyList<CaseResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var faults = results.Where(r => r.Category == FaultCategory).ToList();
        var ambiguous = results.Where(r => r.Category == AmbiguousCategory && r.AsksExpected > 0).ToList();
        var clean = results.Where(r => r.Category == CleanCategory && r.AsksExpected == 0).ToList();
        var refused = results.Where(r => r.Category == ShouldNotGoThroughCategory).ToList();
        var timed = results.Where(r => r.TimeToFirstCardMs is not null).Select(r => (double)r.TimeToFirstCardMs!.Value).ToList();
        var completed = results.Where(r => r.Executions >= 1).ToList();

        return
        [
            Figure(TargetSpecs[0], results.Count, results.Sum(r => r.ExecutionsWithoutTap)),
            // A key without a final names no count: its case adds nothing (every dev fault key has one).
            Figure(TargetSpecs[1], faults.Count, faults.Sum(r => Math.Max(0, r.Executions - (r.ExpectedTransactions ?? r.Executions)))),
            Share(TargetSpecs[2], results.Count, results.Count(r => r.WrongCard)),
            Share(TargetSpecs[3], ambiguous.Count, ambiguous.Count(r => r.AsksHeld)),
            Share(TargetSpecs[4], clean.Count, clean.Count(r => r.AsksObserved > 0)),
            Share(TargetSpecs[5], refused.Count, refused.Count(r => r.CardsIssued == 0)),
            Share(TargetSpecs[6], refused.Count, refused.Count(r => r.RefusalReasonGiven)),
            Figure(TargetSpecs[7], timed.Count, Median(timed)),
            Figure(TargetSpecs[8], completed.Count, completed.Count == 0 ? 0 : (double)completed.Average(r => r.CostUsd)),
        ];
    }

    /// <summary>Each measure across the runs of a repeat series: min, median and max of the defined values, and the verdict on the median.</summary>
    public static IReadOnlyList<RepeatTarget> RepeatTargets(IReadOnlyList<RunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var tables = runs.Select(r => Targets(r.Results)).ToList();
        return
        [
            .. TargetSpecs.Select((spec, i) =>
            {
                var values = tables.Select(t => t[i].Value).OfType<double>().ToList();
                if (values.Count == 0)
                    return new RepeatTarget(spec.Measure, spec.Target, spec.Unit, 0, null, null, null, NotApplicable);
                var median = Median(values);
                return new RepeatTarget(spec.Measure, spec.Target, spec.Unit, values.Count, values.Min(), median, values.Max(), VerdictOf(spec, median));
            }),
        ];
    }

    /// <summary>The console lines of a run's targets table.</summary>
    public static string TargetsConsoleSummary(IReadOnlyList<TargetRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var text = new StringBuilder();
        text.Append("Targets:").Append(Environment.NewLine);
        foreach (var row in rows)
        {
            text.Append("  ").Append(row.Measure).Append(": ").Append(ValueText(row))
                .Append(" (target ").Append(row.Target).Append(", ").Append(Number(row.Cases)).Append(" cases): ")
                .Append(row.Verdict).Append(Environment.NewLine);
        }

        return text.ToString();
    }

    private static void TargetsTable(StringBuilder md, IReadOnlyList<TargetRow> rows)
    {
        md.Append("Targets from docs/targets.md. The verdict is on the point value; a measure with no cases is n/a.\n\n");
        md.Append("| Measure | Target | Cases | Value | Wilson 95% | Verdict |\n");
        md.Append("|---|---|---|---|---|---|\n");
        foreach (var row in rows)
        {
            var interval = row.Value is null ? NotApplicable
                : row.LowPercent is { } low && row.HighPercent is { } high ? Percent(low) + "–" + Percent(high) + " %"
                : "-";
            md.Append("| ").Append(row.Measure)
                .Append(" | ").Append(row.Target)
                .Append(" | ").Append(Number(row.Cases))
                .Append(" | ").Append(ValueText(row))
                .Append(" | ").Append(interval)
                .Append(" | ").Append(row.Verdict).Append(" |\n");
        }
    }

    private static void RepeatTargetsTable(StringBuilder md, IReadOnlyList<RepeatTarget> targets)
    {
        md.Append("Targets from docs/targets.md: each run's value, and the verdict on the median. A run with no cases for a measure is left out of it.\n\n");
        md.Append("| Measure | Target | Min | Median | Max | Verdict on the median |\n");
        md.Append("|---|---|---|---|---|---|\n");
        foreach (var t in targets)
        {
            md.Append("| ").Append(t.Measure)
                .Append(" | ").Append(t.Target)
                .Append(" | ").Append(UnitText(t.Unit, t.Min))
                .Append(" | ").Append(UnitText(t.Unit, t.Median))
                .Append(" | ").Append(UnitText(t.Unit, t.Max))
                .Append(" | ").Append(t.Verdict).Append(" |\n");
        }
    }

    private static string RepeatTargetLine(RepeatTarget t)
        => t.Measure + ": " + UnitText(t.Unit, t.Min) + " / " + UnitText(t.Unit, t.Median) + " / " + UnitText(t.Unit, t.Max)
            + " (target " + t.Target + "): " + t.Verdict;

    private static TargetRow Figure(TargetSpec spec, int cases, double value)
        => cases == 0
            ? new TargetRow(spec.Measure, spec.Target, spec.Unit, 0, null, null, null, null, NotApplicable)
            : new TargetRow(spec.Measure, spec.Target, spec.Unit, cases, null, value, null, null, VerdictOf(spec, value));

    private static TargetRow Share(TargetSpec spec, int cases, int count)
    {
        if (cases == 0)
            return new TargetRow(spec.Measure, spec.Target, spec.Unit, 0, null, null, null, null, NotApplicable);
        var value = 100.0 * count / cases;
        var (low, high) = Wilson(count, cases);
        return new TargetRow(spec.Measure, spec.Target, spec.Unit, cases, count, value, 100 * low, 100 * high, VerdictOf(spec, value));
    }

    private static string VerdictOf(TargetSpec spec, double value)
        => spec.Meets is null ? Reported : spec.Meets(value) ? Pass : Miss;

    private static string ValueText(TargetRow row)
        => row.Unit == PercentUnit && row.Value is { } value && row.Count is { } count
            ? Percent(value) + " % (" + Number(count) + "/" + Number(row.Cases) + ")"
            : UnitText(row.Unit, row.Value);

    private static string UnitText(string unit, double? value) => value switch
    {
        null => NotApplicable,
        { } v when unit == PercentUnit => Percent(v) + " %",
        { } v when unit == MsUnit => v.ToString("0", CultureInfo.InvariantCulture) + " ms",
        { } v when unit == UsdUnit => v.ToString("0.0000", CultureInfo.InvariantCulture) + " USD",
        { } v => v.ToString("0.#", CultureInfo.InvariantCulture),
    };

    /// <summary>A measure of <c>docs/targets.md</c>: its name, target, unit and pass test (null for a measure without a target).</summary>
    private sealed record TargetSpec(string Measure, string Target, string Unit, Func<double, bool>? Meets);
}
