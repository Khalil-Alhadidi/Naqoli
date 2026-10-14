using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Core.Requests;
using Agent.Core.Text;

namespace Agent.Core.Parsing;

/// <summary>One scripted sentence and the request it parses to.</summary>
public sealed record ScriptedSentence(string Sentence, ParsedRequest Request);

/// <summary>
/// The parser the screen journeys and the console use until milestone 6: a whole typed sentence maps to the request
/// written next to it in <c>scripted-sentences.json</c> (an embedded resource; the sentences are Khalil's to write).
/// The typed text and each sentence are compared after <see cref="ArabicText.Normalize"/> (digits to ASCII, whitespace
/// collapsed), ordinal and whole; anything else is <see cref="RequestIntent.Unclear"/>. It never calls a model and
/// holds no state per user, so one instance serves everyone. Its <see cref="LastUsage"/> is zero tokens and zero cost.
/// </summary>
public sealed class ScriptedParser : IParser, IParserUsage
{
    private const string ResourceName = "Agent.Core.Parsing.scripted-sentences.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, ParsedRequest> sentences;

    private ParseUsage? lastUsage;

    /// <summary>The sentences of the embedded <c>scripted-sentences.json</c>.</summary>
    public ScriptedParser()
        : this(EmbeddedSentences())
    {
    }

    /// <summary>
    /// The sentences of the embedded <c>scripted-sentences.json</c>, as written (the screen journeys type them, so they
    /// keep working when Khalil replaces the placeholders).
    /// </summary>
    public static IReadOnlyList<ScriptedSentence> EmbeddedSentences()
    {
        using var stream = typeof(ScriptedParser).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        return JsonSerializer.Deserialize<List<ScriptedSentence>>(stream, Json)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is empty.");
    }

    /// <summary>The given sentences (tests).</summary>
    /// <exception cref="ArgumentException">Two sentences are the same after normalising.</exception>
    public ScriptedParser(IEnumerable<ScriptedSentence> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);

        this.sentences = new Dictionary<string, ParsedRequest>(StringComparer.Ordinal);
        foreach (var sentence in sentences)
        {
            if (!this.sentences.TryAdd(ArabicText.Normalize(sentence.Sentence), sentence.Request))
                throw new ArgumentException($"The scripted sentence '{sentence.Sentence}' is listed twice.", nameof(sentences));
        }
    }

    /// <summary>The last parse: no tokens, no cost, and the time the lookup took.</summary>
    public ParseUsage? LastUsage => Volatile.Read(ref lastUsage);

    public Task<ParsedRequest> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);

        var started = Stopwatch.GetTimestamp();
        var parsed = sentences.TryGetValue(ArabicText.Normalize(text), out var request)
            ? request
            : new ParsedRequest { Intent = RequestIntent.Unclear };
        Volatile.Write(ref lastUsage, new ParseUsage(0, 0, 0m, Stopwatch.GetElapsedTime(started)));
        return Task.FromResult(parsed);
    }
}
