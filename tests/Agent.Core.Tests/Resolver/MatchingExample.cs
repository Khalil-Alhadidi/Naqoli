using System.Text.Json;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Resolver;

/// <summary>
/// One example of the matching rules (<c>matching-examples.json</c>, transcribed from <c>docs/matching-rules.md</c>),
/// tagged by its rule number. Read once and shared by <see cref="ContactResolverTests"/> and the alias rule's tests, so
/// no test holds an Arabic literal.
/// </summary>
internal sealed record MatchingExample(
    int Rule,
    string User,
    string Typed,
    string Expect,
    string? AccountOf,
    string? Kind,
    string[]? Options,
    string? Pending)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Every example, in file order.</summary>
    public static IReadOnlyList<MatchingExample> All { get; } = JsonSerializer.Deserialize<List<MatchingExample>>(
        File.ReadAllText(Path.Combine(SeedHashTests.RepositoryRoot(), "tests", "Agent.Core.Tests", "Resolver", "matching-examples.json")),
        JsonOptions)!;

    /// <summary>The examples of <paramref name="rule"/> that expect a question and are not pending.</summary>
    public static IEnumerable<MatchingExample> QuestionsOf(int rule)
        => All.Where(e => e.Rule == rule && e.Expect == "options" && e.Pending is null);
}
