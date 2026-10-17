using System.Globalization;
using System.Text.Json;

namespace Agent.Core.Text;

/// <summary>
/// Khalil's number words for reading a typed amount (<c>amount-words.json</c>, an embedded resource): the same words as
/// the <c>Words_*</c> keys of the phone's resource file (17 Oct 2026), plus his spelling of millions and the word for
/// dinar. Read once; every word is kept in the form <see cref="ArabicText.Normalize"/> gives, so it compares with
/// normalised input.
/// </summary>
internal static class AmountTextWords
{
    private const string ResourceName = "Agent.Core.Text.amount-words.json";

    private static readonly Lazy<IReadOnlyDictionary<string, AmountWord>> Loaded = new(Load);

    /// <summary>Every word, normalised, with what it stands for.</summary>
    public static IReadOnlyDictionary<string, AmountWord> Words => Loaded.Value;

    /// <summary>The joiner "and", normalised; written as its own word or against the next one.</summary>
    public static string And => Loaded.Value.First(w => w.Value.Kind == AmountWordKind.And).Key;

    private static Dictionary<string, AmountWord> Load()
    {
        using var stream = typeof(AmountTextWords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var document = JsonDocument.Parse(stream);

        var words = new Dictionary<string, AmountWord>(StringComparer.Ordinal);
        void Add(string? text, AmountWord word)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException($"Embedded resource {ResourceName} has a blank word.");
            if (!words.TryAdd(ArabicText.Normalize(text), word))
                throw new InvalidOperationException($"Embedded resource {ResourceName} lists a word twice.");
        }

        void AddList(string property, AmountWordKind kind, int value)
        {
            foreach (var text in document.RootElement.GetProperty(property).EnumerateArray())
                Add(text.GetString(), new AmountWord(kind, value));
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                continue;
            var kind = value switch
            {
                >= 1 and <= 10 => AmountWordKind.Unit,
                >= 11 and <= 19 => AmountWordKind.Teen,
                >= 20 and <= 90 when value % 10 == 0 => AmountWordKind.Tens,
                >= 100 and <= 900 when value % 100 == 0 => AmountWordKind.Hundreds,
                _ => throw new InvalidOperationException($"Embedded resource {ResourceName} has an unknown number {value}."),
            };
            Add(property.Value.GetString(), new AmountWord(kind, value));
        }

        foreach (var construct in document.RootElement.GetProperty("construct").EnumerateObject())
            Add(construct.Value.GetString(), new AmountWord(AmountWordKind.Unit, int.Parse(construct.Name, NumberStyles.None, CultureInfo.InvariantCulture)));

        AddList("thousand", AmountWordKind.Thousand, 1);
        AddList("two_thousand", AmountWordKind.Thousand, 2);
        AddList("thousands", AmountWordKind.Thousand, 0);
        AddList("million", AmountWordKind.Million, 1);
        AddList("two_million", AmountWordKind.Million, 2);
        AddList("millions", AmountWordKind.Million, 0);
        AddList("dinar", AmountWordKind.Dinar, 0);
        Add(document.RootElement.GetProperty("and").GetString(), new AmountWord(AmountWordKind.And, 0));
        return words;
    }
}

/// <summary>What a word of <see cref="AmountTextWords"/> is.</summary>
internal enum AmountWordKind
{
    /// <summary>1 to 10, or a short counting form (3 to 10).</summary>
    Unit,

    /// <summary>11 to 19.</summary>
    Teen,

    /// <summary>20, 30 ... 90.</summary>
    Tens,

    /// <summary>100, 200 ... 900.</summary>
    Hundreds,

    /// <summary>A word for thousand: one (value 1), two (value 2), or the plural after a count (value 0).</summary>
    Thousand,

    /// <summary>A word for million: one (value 1), two (value 2), or the plural after a count (value 0).</summary>
    Million,

    /// <summary>The word for dinar.</summary>
    Dinar,

    /// <summary>The joiner "and".</summary>
    And,
}

/// <summary>One word of <see cref="AmountTextWords"/>: its kind and its value.</summary>
internal readonly record struct AmountWord(AmountWordKind Kind, int Value);
