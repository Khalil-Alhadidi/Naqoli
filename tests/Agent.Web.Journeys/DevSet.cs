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
    /// <summary>The text of say step number <paramref name="step"/> (0 for the first say) of case <paramref name="id"/>.</summary>
    /// <exception cref="InvalidOperationException">The set has no such case, or the case has fewer says.</exception>
    public static string Say(string id, int step = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(step);
        var path = Path.Combine(RepositoryRoot.Find(), "data", ScriptedParser.DevSetFileName);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.GetProperty("id").GetString() != id)
                continue;
            var seen = 0;
            foreach (var element in document.RootElement.GetProperty("steps").EnumerateArray())
            {
                if (!element.TryGetProperty("say", out var say))
                    continue;
                if (seen++ == step)
                    return say.GetString() ?? throw new InvalidOperationException(id + " has a null say step.");
            }

            throw new InvalidOperationException(FormattableString.Invariant($"{id} has no say number {step}."));
        }

        throw new InvalidOperationException("The development set has no case " + id + ".");
    }
}
