namespace Agent.Core.Reports;

/// <summary>Cases, passes and the Wilson 95% interval of one group.</summary>
public sealed record GroupScore(string Name, int Cases, int Passed, double Percent, double LowPercent, double HighPercent);

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

/// <summary>One group across the runs of a repeat series: its case count and the min, median and max of its passes.</summary>
public sealed record RepeatGroup(string Name, int Cases, int MinPassed, double MedianPassed, int MaxPassed);

/// <summary>One run of a repeat series: its passes, its cost and its median case time.</summary>
public sealed record RepeatRun(int Run, int Passed, int Cases, decimal CostUsd, double MedianTimeMs);
