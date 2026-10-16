using System.Text.Json;
using Agent.Core;

namespace Agent.Web.Journeys;

/// <summary>
/// A seed user's PIN, read from <c>data/seed/users.json</c> for the PIN journey. A PIN is typed into the card and searched
/// for in the logs; it is never printed or put in an assertion message.
/// </summary>
internal static class SeedPins
{
    /// <summary>Wrong PINs to choose from: four digits that occur in no seeded reference, amount or balance.</summary>
    private static readonly string[] WrongCandidates = ["8642", "9753"];

    /// <summary>The PIN of seed user <paramref name="userCode"/> (<c>U3</c>).</summary>
    public static string PinOf(string userCode)
    {
        var path = Path.Combine(RepositoryRoot.Find(), "data", "seed", "users.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var user in document.RootElement.EnumerateArray())
        {
            if (user.GetProperty("id").GetString() == userCode)
                return user.GetProperty("pin").GetString() ?? throw new InvalidOperationException("A seed user has a null PIN.");
        }

        throw new InvalidOperationException("No seed user " + userCode + ".");
    }

    /// <summary>A well-formed PIN that is not <paramref name="userCode"/>'s.</summary>
    public static string WrongPinOf(string userCode)
    {
        var pin = PinOf(userCode);
        return WrongCandidates.First(candidate => !string.Equals(candidate, pin, StringComparison.Ordinal));
    }
}
