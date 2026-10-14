using System.Text.Json;

namespace Agent.Core.Text;

/// <summary>
/// Khalil's word lists from matching rule 1 (<c>arabic-words.json</c>, an embedded resource): kinship groups and title
/// words. Read once; the lists are kept in normalised form so they compare with normalised input.
/// </summary>
internal static class ArabicWords
{
    private const string ResourceName = "Agent.Core.Text.arabic-words.json";

    private static readonly Lazy<Lists> Loaded = new(Load);

    /// <summary>Normalised kinship variant to its normalised canonical form (a canonical form maps to itself).</summary>
    public static IReadOnlyDictionary<string, string> KinshipVariants => Loaded.Value.KinshipVariants;

    /// <summary>The normalised canonical kinship forms.</summary>
    public static IReadOnlySet<string> KinshipCanonicals => Loaded.Value.KinshipCanonicals;

    /// <summary>The normalised title words.</summary>
    public static IReadOnlySet<string> Titles => Loaded.Value.Titles;

    private sealed record Lists(
        IReadOnlyDictionary<string, string> KinshipVariants,
        IReadOnlySet<string> KinshipCanonicals,
        IReadOnlySet<string> Titles);

    private sealed record WordsFile(KinshipGroup[] Kinship, string[] Titles);

    private sealed record KinshipGroup(string Canonical, string[] Variants);

    private static Lists Load()
    {
        using var stream = typeof(ArabicWords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        var file = JsonSerializer.Deserialize<WordsFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is empty.");

        var variants = new Dictionary<string, string>(StringComparer.Ordinal);
        var canonicals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in file.Kinship)
        {
            var canonical = ArabicText.NormalizeLetters(group.Canonical);
            canonicals.Add(canonical);
            variants[canonical] = canonical;
            foreach (var variant in group.Variants)
                variants[ArabicText.NormalizeLetters(variant)] = canonical;
        }

        var titles = file.Titles.Select(ArabicText.NormalizeLetters).ToHashSet(StringComparer.Ordinal);
        return new Lists(variants, canonicals, titles);
    }
}
