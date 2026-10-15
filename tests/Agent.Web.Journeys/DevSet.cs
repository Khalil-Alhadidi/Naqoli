using System.Text.Json;
using Agent.Core;
using Agent.Core.Parsing;

namespace Agent.Web.Journeys;

/// <summary>
/// The development set, read by case id: the journeys type a case's message exactly as Khalil wrote it, and the
/// console's scripted parser (the oracle over the same set) returns that case's parse.
/// </summary>
internal static class DevSet
{
    /// <summary>The text of the first <c>say</c> step of case <paramref name="id"/>.</summary>
    /// <exception cref="InvalidOperationException">The set has no such case, or the case says nothing.</exception>
    public static string Say(string id)
    {
        var path = Path.Combine(RepositoryRoot.Find(), "data", ScriptedParser.DevSetFileName);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.GetProperty("id").GetString() != id)
                continue;
            foreach (var step in document.RootElement.GetProperty("steps").EnumerateArray())
            {
                if (step.TryGetProperty("say", out var say))
                    return say.GetString() ?? throw new InvalidOperationException(id + " has a null say step.");
            }

            throw new InvalidOperationException(id + " has no say step.");
        }

        throw new InvalidOperationException("The development set has no case " + id + ".");
    }
}
