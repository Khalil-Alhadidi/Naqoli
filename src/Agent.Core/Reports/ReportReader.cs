using System.Text.Json;

namespace Agent.Core.Reports;

/// <summary>
/// Reads the JSON reports <c>Agent.Eval</c> commits under <c>docs/reports</c> (milestone 10), for the console's Results
/// panel. It reads figures and codes only (a report never holds case text) and writes nothing. The 27 reports of 15 and
/// 16 Oct 2026 predate the targets table: <see cref="List"/> skips and counts them.
/// </summary>
public static class ReportReader
{
    /// <summary>The single-run and aggregate reports of the development and held-out sets, by name.</summary>
    private static readonly string[] Patterns = ["dev-*.json", "heldout-*.json"];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// <paramref name="reportsDirectory"/> as it is when rooted, otherwise under the repository root
    /// (<see cref="RepositoryRoot.Find"/>).
    /// </summary>
    public static string Resolve(string reportsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportsDirectory);
        return Path.IsPathRooted(reportsDirectory) ? reportsDirectory : Path.Combine(RepositoryRoot.Find(), reportsDirectory);
    }

    /// <summary>
    /// Every <c>dev-*.json</c> and <c>heldout-*.json</c> in <paramref name="directory"/> that reads and has a targets
    /// table, newest first (<see cref="ReportEntry.StartedUtc"/> descending, an aggregate before a single run on a tie,
    /// then the file name descending, ordinal). Any other file of those names is counted in
    /// <see cref="ReportListing.Skipped"/>; a missing directory is an empty listing. The default is the first entry whose
    /// parser ends with <c>-</c> and <paramref name="candidate"/>, else the first entry. A file's content never makes it throw.
    /// </summary>
    public static ReportListing List(string directory, string candidate)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!Directory.Exists(directory))
            return new ReportListing([], 0, null);

        var entries = new List<ReportEntry>();
        var skipped = 0;
        foreach (var path in Patterns.SelectMany(p => Directory.EnumerateFiles(directory, p)))
        {
            try
            {
                var report = Read(path);
                if (report.TargetCount == 0)
                {
                    skipped++;
                    continue;
                }

                var header = report.Header;
                entries.Add(new ReportEntry(report.FileName, header.Set, header.Parser, header.StartedUtc, report.Repeat is not null));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        var ordered = entries
            .OrderByDescending(e => e.StartedUtc)
            .ThenByDescending(e => e.IsAggregate)
            .ThenByDescending(e => e.FileName, StringComparer.Ordinal)
            .ToList();
        var suffix = "-" + candidate;
        var chosen = ordered.FirstOrDefault(e => e.Parser.EndsWith(suffix, StringComparison.Ordinal)) ?? ordered.FirstOrDefault();
        return new ReportListing(ordered, skipped, chosen?.FileName);
    }

    /// <summary>
    /// Reads one report: an aggregate (<see cref="ReportFile.Repeat"/>) when its header has <c>runs</c>, otherwise a
    /// single run (<see cref="ReportFile.Single"/>).
    /// </summary>
    /// <exception cref="JsonException">The file is not a report: not JSON, or no header with a set and a parser.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static ReportFile Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("header", out var header)
            || header.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The report has no header object.");
        }

        var name = Path.GetFileName(path);
        var report = header.TryGetProperty("runs", out var runs) && runs.ValueKind != JsonValueKind.Null
            ? new ReportFile(name, null, root.Deserialize<RepeatReportDocument>(Json) ?? throw new JsonException("The report is null."))
            : new ReportFile(name, root.Deserialize<ReportDocument>(Json) ?? throw new JsonException("The report is null."), null);

        // The listing reads these fields of every file: a report without them is not one this reader knows.
        var read = report.Single?.Header ?? report.Repeat?.Header;
        if (read is not { Set: not null, Parser: not null })
            throw new JsonException("The report's header has no set or parser.");
        return report;
    }
}
