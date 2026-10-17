using System.Text.Json;
using Agent.Core.Parsing;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests;

/// <summary>
/// Text from the development set and its sidecar of parses, read by case id, so a test types a message exactly as Khalil
/// wrote it without an Arabic literal in C#. Only <c>requests.dev.jsonl</c> is read (rule 7).
/// </summary>
internal static class DevSetText
{
    private static string DataPath(string file) => Path.Combine(SeedHashTests.RepositoryRoot(), "data", file);

    /// <summary>The text of say step number <paramref name="step"/> (0 for the first say) of case <paramref name="id"/>.</summary>
    public static string Say(string id, int step = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(step);
        using var document = Line(ScriptedParser.DevSetFileName, root => root.GetProperty("id").GetString() == id);
        var says = document.RootElement.GetProperty("steps").EnumerateArray()
            .Where(s => s.TryGetProperty("say", out _))
            .Select(s => s.GetProperty("say").GetString()!)
            .ToList();
        return step < says.Count
            ? says[step]
            : throw new InvalidOperationException(FormattableString.Invariant($"{id} has no say number {step}."));
    }

    /// <summary>The recipient text of the first request in the sidecar's parse of say step <paramref name="step"/> of <paramref name="id"/>.</summary>
    public static string? SidecarRecipient(string id, int step = 0)
    {
        using var document = Line(
            ScriptedParser.SidecarFileName,
            root => root.GetProperty("id").GetString() == id && root.GetProperty("step").GetInt32() == step);
        return document.RootElement.GetProperty("parse").GetProperty("requests")[0].GetProperty("recipient_text").GetString();
    }

    /// <summary>The endearments the console is configured with (<c>src/Agent.Web/appsettings.json</c>), never a literal.</summary>
    public static string[] ConfiguredEndearments() => ConfiguredWords("Endearments");

    /// <summary>The filler words the console is configured with (<c>src/Agent.Web/appsettings.json</c>), never a literal.</summary>
    public static string[] ConfiguredFillerWords() => ConfiguredWords("FillerWords");

    private static string[] ConfiguredWords(string setting)
    {
        var path = Path.Combine(SeedHashTests.RepositoryRoot(), "src", "Agent.Web", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.GetProperty("Agent").GetProperty(setting).EnumerateArray().Select(e => e.GetString()!)];
    }

    private static JsonDocument Line(string file, Func<JsonElement, bool> match)
    {
        foreach (var line in File.ReadLines(DataPath(file)))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var document = JsonDocument.Parse(line);
            if (match(document.RootElement))
                return document;
            document.Dispose();
        }

        throw new InvalidOperationException("No matching line in " + file + ".");
    }
}
