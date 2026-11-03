namespace Agent.Core.Reports;

/// <summary>The header of a committed report: what was run, on which file, with which parser, when and with which settings.</summary>
/// <param name="Set"><c>dev</c> or <c>heldout</c>.</param>
/// <param name="SetFileSha256">The SHA-256 of the case file, lower-case hex.</param>
/// <param name="Parser">The parser's name, such as <c>scripted</c> or <c>claude-sonnet</c>.</param>
/// <param name="Runs">The number of runs of an aggregate (<c>--repeat N</c>) report, or null for a single run.</param>
/// <param name="StartedUtc">When the run (an aggregate's first run) began, UTC.</param>
/// <param name="Settings">The run's settings, by key (no keys or secrets).</param>
public sealed record ReportHeader(
    string Set,
    string SetFileSha256,
    string Parser,
    int? Runs,
    DateTime StartedUtc,
    IReadOnlyDictionary<string, string> Settings);

/// <summary>The median and 90th-percentile case time of a run.</summary>
public sealed record ReportTime(double MedianMs, double P90Ms);

/// <summary>
/// The part of a single run's JSON report the console shows. <paramref name="Targets"/> is null in the reports written
/// before the targets table (15 and 16 Oct 2026).
/// </summary>
public sealed record ReportDocument(
    ReportHeader Header,
    GroupScore Totals,
    IReadOnlyList<TargetRow>? Targets,
    ReportTime Time,
    decimal CostUsd);

/// <summary>
/// The part of an aggregate (<c>--repeat N</c>) JSON report the console shows. <paramref name="Targets"/> is null in a
/// report written before the targets table.
/// </summary>
public sealed record RepeatReportDocument(
    ReportHeader Header,
    IReadOnlyList<RepeatGroup> Groups,
    IReadOnlyList<RepeatRun> Runs,
    IReadOnlyList<RepeatTarget>? Targets);

/// <summary>One report file read: exactly one of <paramref name="Single"/> and <paramref name="Repeat"/> is set.</summary>
public sealed record ReportFile(string FileName, ReportDocument? Single, RepeatReportDocument? Repeat)
{
    /// <summary>The header of whichever document the file holds.</summary>
    public ReportHeader Header => Single?.Header ?? Repeat?.Header
        ?? throw new InvalidOperationException("A report file holds neither a single run nor an aggregate.");

    /// <summary>The number of target rows, 0 when the report has no targets table.</summary>
    public int TargetCount => Single?.Targets?.Count ?? Repeat?.Targets?.Count ?? 0;
}

/// <summary>One readable report in a listing.</summary>
public sealed record ReportEntry(string FileName, string Set, string Parser, DateTime StartedUtc, bool IsAggregate);

/// <summary>
/// The readable reports of a directory, newest first, the number of files skipped (no targets table, or not readable),
/// and the file the console opens first, or null when there is none.
/// </summary>
public sealed record ReportListing(IReadOnlyList<ReportEntry> Entries, int Skipped, string? DefaultFileName);
