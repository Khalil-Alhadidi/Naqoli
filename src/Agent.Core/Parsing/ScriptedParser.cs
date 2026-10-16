using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Agent.Core.Masking;
using Agent.Core.Requests;
using Agent.Core.Text;

namespace Agent.Core.Parsing;

/// <summary>One scripted sentence and the request it parses to (tests).</summary>
public sealed record ScriptedSentence(string Sentence, ParsedRequest Request);

/// <summary>
/// The parser the screen journeys use, and the console's default: an oracle over the development set. An exact dev-set
/// message returns that case's expected parse, written by hand in <c>requests.dev.parses.jsonl</c> next to the set
/// (<see cref="Load"/>); anything else is <see cref="RequestIntent.Unclear"/>. Each message is masked
/// (<see cref="Masker"/>) as the typed text is, and both are compared after <see cref="ArabicText.Normalize"/> (digits to
/// ASCII, whitespace collapsed), ordinal and whole. It never calls a model and holds no state per user, so one instance
/// serves everyone. Its <see cref="LastUsage"/> is zero tokens and zero cost.
/// </summary>
public sealed class ScriptedParser : IParser, IParserUsage
{
    /// <summary>The only file name <see cref="Load"/> accepts (rule 7: the held-out set is never opened).</summary>
    public const string DevSetFileName = "requests.dev.jsonl";

    /// <summary>The parses of the development set, one line per <c>say</c> step, in the same directory.</summary>
    public const string SidecarFileName = "requests.dev.parses.jsonl";

    /// <summary>The name of this parser on the trace and in the eval report.</summary>
    public const string ParserName = "scripted";

    // As the Claude parser reads a reply: snake_case names from the DTO's attributes, an unknown field refused (the DTO's
    // JsonUnmappedMemberHandling), a field named twice refused.
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.General)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        AllowDuplicateProperties = false,
    };

    private readonly Dictionary<string, ParsedRequest> sentences;

    private ParseUsage? lastUsage;

    /// <summary>The given sentences (tests).</summary>
    /// <exception cref="ArgumentException">Two sentences are the same after normalising.</exception>
    public ScriptedParser(IEnumerable<ScriptedSentence> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);

        this.sentences = new Dictionary<string, ParsedRequest>(StringComparer.Ordinal);
        foreach (var sentence in sentences)
        {
            // The conversation masks the typed text before the parse call, so each sentence is masked the same way: a
            // sentence may hold a real-looking number, and the typed text matches it in any spacing or digit form.
            if (!this.sentences.TryAdd(ArabicText.Normalize(Masker.Mask(sentence.Sentence).Text), sentence.Request))
                throw new ArgumentException($"The scripted sentence '{sentence.Sentence}' is listed twice.", nameof(sentences));
        }
    }

    private ScriptedParser(Dictionary<string, ParsedRequest> sentences) => this.sentences = sentences;

    public string Name => ParserName;

    /// <summary>The last parse: no tokens, no cost, and the time the lookup took.</summary>
    public ParseUsage? LastUsage => Volatile.Read(ref lastUsage);

    /// <summary>
    /// The oracle over the development set at <paramref name="devSetPath"/> and its sidecar
    /// <see cref="SidecarFileName"/> in the same directory. Only <c>id</c> and <c>steps[].say</c> of the set are read.
    /// Each sidecar line is <c>{"id", "step" (0-based among the case's steps), "say", "parse"}</c>, where <c>parse</c>
    /// is the structure the model returns (<see cref="ParsedRequestsDto"/>) with a typed number written as typed. At load
    /// each <c>say</c> is masked, every number in the parse's texts becomes its token, and
    /// <see cref="ParsedRequestValidator"/> checks the parse against the message's map; its first request is what the
    /// message returns.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The file name is not exactly <see cref="DevSetFileName"/> (nothing is opened), or the set or the sidecar is a
    /// symbolic link.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The files disagree: a sidecar line for a step that does not say, or whose text differs from the set's; a say step
    /// with no line; a parse the validator refuses; the same message with two different parses. The message names the
    /// case and the step, never the text.
    /// </exception>
    public static ScriptedParser Load(string devSetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devSetPath);
        var full = Path.GetFullPath(devSetPath);
        if (!string.Equals(Path.GetFileName(full), DevSetFileName, StringComparison.Ordinal))
            throw new ArgumentException("The scripted parser loads only a file named " + DevSetFileName + ".", nameof(devSetPath));

        var sidecar = Path.Combine(Path.GetDirectoryName(full)!, SidecarFileName);

        // The name check holds only if the name is the file: a symbolic link (or junction) named requests.dev.jsonl could
        // point anywhere, so neither file may be one. (A hard link has no link target and cannot be told from a file.)
        foreach (var file in new[] { full, sidecar })
        {
            if (new FileInfo(file).LinkTarget is not null)
                throw new ArgumentException("The scripted parser does not follow a link: " + Path.GetFileName(file) + " is one.", nameof(devSetPath));
        }

        var says = ReadSays(full);

        var sentences = new Dictionary<string, ParsedRequest>(StringComparer.Ordinal);
        var parseJson = new Dictionary<string, string>(StringComparer.Ordinal);
        var covered = new HashSet<(string Id, int Step)>();
        var number = 0;
        foreach (var text in File.ReadLines(sidecar))
        {
            number++;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var line = ReadLine(text, number);
            var where = SidecarFileName + " line " + Invariant(number) + " (" + line.Id + " step " + Invariant(line.Step) + ")";
            if (!says.TryGetValue((line.Id!, line.Step!.Value), out var say))
                throw new InvalidDataException(where + ": the development set has no say step there.");
            if (!string.Equals(say, line.Say, StringComparison.Ordinal))
                throw new InvalidDataException(where + ": the text differs from the development set's.");
            if (!covered.Add((line.Id!, line.Step!.Value)))
                throw new InvalidDataException(where + ": the step has a line already.");

            var masked = Masker.Mask(say);
            var parse = new ParsedRequestsDto(line.Parse!.Requests?.Select(r => r is null ? r! : Tokenize(r, masked.Map)).ToList());
            var result = ParsedRequestValidator.Validate(parse, masked.Map);
            if (result.Problem is not null)
                throw new InvalidDataException(where + ": the parse breaks a rule of the structure (" + result.Problem + ").");

            var key = ArabicText.Normalize(masked.Text);
            var json = JsonSerializer.Serialize(parse, StrictJson);
            if (parseJson.TryGetValue(key, out var earlier))
            {
                if (!string.Equals(earlier, json, StringComparison.Ordinal))
                    throw new InvalidDataException(where + ": the same message has a different parse on an earlier line.");
                continue;
            }

            parseJson.Add(key, json);
            sentences.Add(key, result.Request);
        }

        var missing = says.Keys.Where(k => !covered.Contains(k)).OrderBy(k => k.Id, StringComparer.Ordinal).ThenBy(k => k.Step).ToList();
        if (missing.Count > 0)
        {
            var (id, step) = missing[0];
            throw new InvalidDataException(
                SidecarFileName + ": " + id + " step " + Invariant(step) + " has no line (" + Invariant(missing.Count) + " say step(s) without one).");
        }

        return new ScriptedParser(sentences);
    }

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

    /// <summary>The text of every say step of the set, by case id and step index.</summary>
    private static Dictionary<(string Id, int Step), string> ReadSays(string devSetPath)
    {
        var says = new Dictionary<(string Id, int Step), string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var text in File.ReadLines(devSetPath))
        {
            number++;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            string? id;
            var steps = new List<string?>();
            try
            {
                using var document = JsonDocument.Parse(text);
                id = document.RootElement.GetProperty("id").GetString();
                foreach (var element in document.RootElement.GetProperty("steps").EnumerateArray())
                {
                    steps.Add(element.ValueKind == JsonValueKind.Object
                        && element.TryGetProperty("say", out var say) && say.ValueKind == JsonValueKind.String
                            ? say.GetString()
                            : null);
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new InvalidDataException(DevSetFileName + " line " + Invariant(number) + ": not a case with an id and steps.", ex);
            }

            if (id is null || !ids.Add(id))
                throw new InvalidDataException(DevSetFileName + " line " + Invariant(number) + ": the id is missing or listed twice.");
            for (var step = 0; step < steps.Count; step++)
            {
                if (steps[step] is { } say)
                    says.Add((id, step), say);
            }
        }

        return says;
    }

    private static SidecarLine ReadLine(string text, int number)
    {
        SidecarLine? line;
        try
        {
            line = JsonSerializer.Deserialize<SidecarLine>(text, StrictJson);
        }
        catch (JsonException ex)
        {
            // A field the structure does not name, or a value of the wrong kind: the message names the line, and the case
            // and step when they can be read; the serializer's own message (which may quote the text) stays inside.
            throw new InvalidDataException(SidecarFileName + " line " + Invariant(number) + Where(text) + ": not a line of the structure.", ex);
        }

        if (line is not { Id: not null, Step: not null, Say: not null, Parse: not null })
            throw new InvalidDataException(SidecarFileName + " line " + Invariant(number) + ": id, step, say and parse are required.");
        return line;
    }

    /// <summary>" (id step n)" of a line the strict read refused, as far as they can be read; empty otherwise.</summary>
    private static string Where(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
                return string.Empty;
            var step = root.TryGetProperty("step", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var value) ? Invariant(value) : "?";
            return " (" + id.GetString() + " step " + step + ")";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>The request with each number of <paramref name="map"/> in its texts replaced by its token.</summary>
    private static ParsedRequestDto Tokenize(ParsedRequestDto request, MaskMap map) => request with
    {
        Intent = Tokenize(request.Intent, map),
        RecipientText = Tokenize(request.RecipientText, map),
        AmountUnit = Tokenize(request.AmountUnit, map),
        BillerCode = Tokenize(request.BillerCode, map),
        BillerText = Tokenize(request.BillerText, map),
        BillLabelText = Tokenize(request.BillLabelText, map),
        OperatorText = Tokenize(request.OperatorText, map),
        ProductType = Tokenize(request.ProductType, map),
        UnsupportedReason = Tokenize(request.UnsupportedReason, map),
        CurrencyText = Tokenize(request.CurrencyText, map),
    };

    private static string? Tokenize(string? text, MaskMap map) => text is null ? null : map.Tokenize(text);

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    /// <summary>One line of the sidecar.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SidecarLine(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("step")] int? Step,
        [property: JsonPropertyName("say")] string? Say,
        [property: JsonPropertyName("parse")] ParsedRequestsDto? Parse);
}
