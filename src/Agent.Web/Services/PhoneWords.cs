namespace Agent.Web.Services;

/// <summary>
/// The D6 word lists of the resource file (<c>Word_yes</c>, <c>Word_no</c>): one string, words separated by a Latin or
/// an Arabic comma. Split, trimmed, empties dropped, duplicates dropped. The same lists are repeated in
/// <c>src/Agent.Eval/appsettings.json</c> for the runner, which has no resource file; a test keeps them equal.
/// </summary>
public static class PhoneWords
{
    private static readonly char[] Separators = [',', '،'];

    /// <summary>The words of <paramref name="list"/>, in order, without blanks or repeats.</summary>
    public static string[] Split(string? list)
        => string.IsNullOrWhiteSpace(list)
            ? []
            : [.. list.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];
}
