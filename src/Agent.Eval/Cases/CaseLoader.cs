using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Agent.Eval.Cases;

/// <summary>A case file that cannot be read: names the line and, when it is known, the case id, never the case text.</summary>
public sealed class CaseFileException : Exception
{
    public CaseFileException()
    {
    }

    public CaseFileException(string message)
        : base(message)
    {
    }

    public CaseFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CaseFileException(string path, int line, string? caseId, string problem, Exception? innerException = null)
        : base(Describe(path, line, caseId, problem), innerException)
    {
        Line = line;
        CaseId = caseId;
    }

    /// <summary>Several problems of one file as one exception: every message on its own line, the first one's line and case id.</summary>
    public CaseFileException(IReadOnlyList<CaseFileException> problems)
        : base(string.Join(Environment.NewLine, (problems ?? throw new ArgumentNullException(nameof(problems))).Select(p => p.Message)), problems[0])
    {
        Line = problems[0].Line;
        CaseId = problems[0].CaseId;
    }

    /// <summary>The 1-based line of the problem.</summary>
    public int Line { get; }

    /// <summary>The id of the case on that line, when it could be read.</summary>
    public string? CaseId { get; }

    private static string Describe(string path, int line, string? caseId, string problem)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)} line {line}{(caseId is null ? string.Empty : " (case " + caseId + ")")}: {problem}");
}

/// <summary>
/// Reads a request set (<c>requests.dev.jsonl</c> format, <c>data/README.md</c>): one JSON object per line, blank lines
/// skipped. Strict: snake_case names, no unknown key (<see cref="JsonUnmappedMemberHandling.Disallow"/>), required
/// fields present, nulls only where the format allows them, and the step forms and fault codes of the README. Every line
/// is read before the loader refuses, so one <see cref="CaseFileException"/> names the first problem of each bad line
/// with its line and case id; the message never quotes case text.
/// </summary>
public static class CaseLoader
{
    /// <summary>The six fault codes of <c>settings.json</c>.</summary>
    public static readonly IReadOnlySet<string> FaultCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "wallet_timeout_after_execute",
        "wallet_timeout_before_execute",
        "wallet_error",
        "model_unreachable",
        "model_invalid_reply",
        "external_debit",
    };

    private static readonly HashSet<string> Taps = new(StringComparer.Ordinal) { "option", "confirm", "cancel", "try_again", "save_alias" };
    private static readonly HashSet<string> Waits = new(StringComparer.Ordinal) { "expire", "expire_if_card" };
    private static readonly HashSet<string> Pins = new(StringComparer.Ordinal) { "correct", "wrong" };
    private static readonly HashSet<string> FaultMoments = new(StringComparer.Ordinal) { "request", "confirm", "card_waiting" };

    private const string NoCardPrefix = "no_card_";
    private const string AliasOfferKey = "alias_offer";
    private const string ExpectKey = "expect";

    /// <summary>The options every case file is read with.</summary>
    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>Every case of the file at <paramref name="path"/>, in file order, read strictly.</summary>
    /// <exception cref="CaseFileException">The file is missing, or one or more lines are not valid cases or repeat an id.</exception>
    public static IReadOnlyList<EvalCase> Load(string path) => Load(path, tolerant: false);

    /// <summary>
    /// Every case of the file at <paramref name="path"/>, in file order. With <paramref name="tolerant"/> (the held-out
    /// set, which nobody may open or correct; Khalil, 6 Nov 2026) a part of the answer key the runner cannot read is
    /// dropped and named in <see cref="EvalCase.NotRead"/> instead of refusing the file: a member the format does not
    /// know, a single value of the wrong kind, an unknown step expectation. What decides how the case is played or what
    /// it is at all still refuses the file: a step form, a fault, a missing required field, a block of the wrong shape.
    /// </summary>
    /// <exception cref="CaseFileException">The file is missing, or one or more lines are not valid cases or repeat an id.</exception>
    public static IReadOnlyList<EvalCase> Load(string path, bool tolerant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new CaseFileException($"{Path.GetFileName(path)}: the case file does not exist.");

        var cases = new List<EvalCase>();
        var problems = new List<CaseFileException>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                cases.Add(ReadLine(path, number, line, ids, tolerant));
            }
            catch (CaseFileException ex)
            {
                // Keep reading: a sealed set must name every bad line in one attempt (the held-out file, 6 Nov 2026).
                problems.Add(ex);
            }
        }

        return problems.Count switch
        {
            0 => cases,
            1 => throw problems[0],
            _ => throw new CaseFileException(problems),
        };
    }

    /// <summary>The case on one line, checked; <paramref name="ids"/> gains its id.</summary>
    private static EvalCase ReadLine(string path, int number, string line, HashSet<string> ids, bool tolerant)
    {
        var id = TryReadId(line);
        List<string>? notRead = tolerant ? [] : null;
        EvalCase? read;
        try
        {
            read = notRead is null ? JsonSerializer.Deserialize<EvalCase>(line, Json) : ReadTolerant(line, notRead);
        }
        catch (JsonException ex)
        {
            // The serializer's message names the JSON path and the member, never the value.
            throw new CaseFileException(path, number, id, "not a valid case: " + ex.Message, ex);
        }

        if (read is null)
            throw new CaseFileException(path, number, id, "the line is null.");

        var problem = Validate(read, notRead, out var checkedCase);
        if (problem is not null)
            throw new CaseFileException(path, number, read.Id, problem);
        if (!ids.Add(checkedCase.Id))
            throw new CaseFileException(path, number, checkedCase.Id, "the id is used twice.");
        return notRead is { Count: > 0 } ? checkedCase with { NotRead = notRead } : checkedCase;
    }

    /// <summary>The case on <paramref name="line"/> after <see cref="Strip"/>, read with the strict options.</summary>
    private static EvalCase? ReadTolerant(string line, List<string> notRead)
    {
        try
        {
            var node = JsonNode.Parse(line);
            Strip(node, typeof(EvalCase), string.Empty, notRead);
            return node.Deserialize<EvalCase>(Json);
        }
        catch (ArgumentException ex)
        {
            // A member written twice in one object: the node tree refuses it when the object is walked.
            throw new JsonException("a member is written twice in one object.", ex);
        }
    }

    /// <summary>
    /// Removes from <paramref name="node"/> what <paramref name="type"/> cannot hold, and names each removal in
    /// <paramref name="notRead"/>: a member the record does not have (unless the record keeps unknown members itself,
    /// as <see cref="StepExpect"/> does), and a member whose single value does not read as its type. Records and lists
    /// of records are walked; a record or a list of the wrong shape is left in place for the strict read to refuse.
    /// Names follow the grader's fields: the <c>expect</c> level is not written, list items carry their index.
    /// </summary>
    private static void Strip(JsonNode? node, Type type, string path, List<string> notRead)
    {
        var info = Json.GetTypeInfo(type);
        if (info.Kind == JsonTypeInfoKind.Enumerable)
        {
            if (node is JsonArray array && info.ElementType is { } element && IsRecord(element))
            {
                for (var i = 0; i < array.Count; i++)
                    Strip(array[i], element, path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", notRead);
            }

            return;
        }

        if (info.Kind != JsonTypeInfoKind.Object || node is not JsonObject members)
            return;

        var keepsUnknown = info.Properties.Any(p => p.IsExtensionData);
        foreach (var (name, child) in members.ToList())
        {
            var named = path.Length == 0 ? name : path + "." + name;
            var property = info.Properties.FirstOrDefault(p => !p.IsExtensionData && string.Equals(p.Name, name, StringComparison.Ordinal));
            if (property is null)
            {
                if (!keepsUnknown)
                {
                    members.Remove(name);
                    notRead.Add(named);
                }

                continue;
            }

            if (IsRecord(property.PropertyType) || IsRecordList(property.PropertyType))
            {
                Strip(child, property.PropertyType, name == ExpectKey ? path : named, notRead);
            }
            else if (!Reads(child, property.PropertyType))
            {
                members.Remove(name);
                notRead.Add(named);
            }
        }
    }

    private static bool IsRecord(Type type) => Json.GetTypeInfo(type).Kind == JsonTypeInfoKind.Object;

    private static bool IsRecordList(Type type)
        => Json.GetTypeInfo(type) is { Kind: JsonTypeInfoKind.Enumerable, ElementType: { } element } && IsRecord(element);

    /// <summary>True when <paramref name="value"/> alone reads as <paramref name="type"/>.</summary>
    private static bool Reads(JsonNode? value, Type type)
    {
        try
        {
            _ = JsonSerializer.Deserialize(value?.ToJsonString() ?? "null", type, Json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The case id of a line, when the line is a JSON object with a string <c>id</c>.</summary>
    private static string? TryReadId(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The first problem of <paramref name="evalCase"/>, or null; <paramref name="checkedCase"/> has the <c>no_card_N</c>
    /// keys parsed. With <paramref name="notRead"/> (a tolerant load) a step expectation that cannot be read is dropped
    /// and named there instead of being a problem.
    /// </summary>
    private static string? Validate(EvalCase evalCase, List<string>? notRead, out EvalCase checkedCase)
    {
        checkedCase = evalCase;
        if (string.IsNullOrWhiteSpace(evalCase.Id))
            return "the id is empty.";
        if (string.IsNullOrWhiteSpace(evalCase.User))
            return "the user is empty.";
        if (evalCase.Steps.Count == 0)
            return "the case has no steps.";

        var steps = new List<CaseStep>(evalCase.Steps.Count);
        for (var i = 0; i < evalCase.Steps.Count; i++)
        {
            var step = evalCase.Steps[i];
            var problem = ValidateStep(step, string.Create(CultureInfo.InvariantCulture, $"steps[{i}]"), notRead, out var checkedStep);
            if (problem is not null)
                return string.Create(CultureInfo.InvariantCulture, $"step {i + 1}: {problem}");
            steps.Add(checkedStep);
        }

        if (evalCase.Fault is { } fault)
        {
            if (!FaultCodes.Contains(fault.Code))
                return "unknown fault code '" + fault.Code + "'.";
            if (!FaultMoments.Contains(fault.On))
                return "unknown fault moment '" + fault.On + "'.";
            if (fault.Times is <= 0)
                return "fault times must be at least 1.";
            if (fault.Code == "external_debit" && fault.Amount is not > 0)
                return "an external debit needs a positive amount.";
        }

        if (evalCase.Expect.Asks is { } asks)
        {
            foreach (var ask in asks)
            {
                if (ask.Answer is not null and not "tap" and not "typed")
                    return "unknown ask answer '" + ask.Answer + "'.";
                if (ask.Options is { } options && options.Any(o => o.ValueKind is not JsonValueKind.String and not JsonValueKind.Number))
                    return "an ask option must be a string or a number.";
            }
        }

        checkedCase = evalCase with { Steps = steps };
        return null;
    }

    private static string? ValidateStep(CaseStep step, string name, List<string>? notRead, out CaseStep checkedStep)
    {
        checkedStep = step;
        var forms = (step.Say is null ? 0 : 1) + (step.Tap is null ? 0 : 1) + (step.Wait is null ? 0 : 1);
        if (forms != 1)
            return "exactly one of say, tap and wait must be set.";
        if (step.Tap is { } tap && !Taps.Contains(tap))
            return "unknown tap '" + tap + "'.";
        if (step.Wait is { } wait && !Waits.Contains(wait))
            return "unknown wait '" + wait + "'.";
        if (step.Tap == "option" && step.Value is not { ValueKind: JsonValueKind.String or JsonValueKind.Number })
            return "an option tap needs a string or number value.";
        if (step.Tap != "option" && step.Value is not null)
            return "only an option tap takes a value.";
        if (step.Pin is { } pin && (!Pins.Contains(pin) || step.Tap != "confirm"))
            return "a pin is correct or wrong, on a confirm tap only.";
        if (step.Twice && step.Tap != "confirm")
            return "twice is for a confirm tap only.";

        if (step.Expect is { Amount: not null, Reply: null } lone)
        {
            if (notRead is null)
                return "an expect amount is the figure of a reply; the step names no reply.";
            notRead.Add(name + ".amount");
            step = step with { Expect = lone with { Amount = null } };
            checkedStep = step;
        }

        if (step.Expect is not { Extra: { Count: > 0 } extra } expect)
            return null;

        var noCard = new Dictionary<int, bool>();
        var aliasOfferSet = false;
        string? aliasOffer = null;
        foreach (var (key, value) in extra)
        {
            // Read raw, so an explicit null (no offer) differs from an absent key (not graded).
            if (key == AliasOfferKey)
            {
                if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                {
                    if (notRead is null)
                        return "alias_offer is a text or null.";
                    notRead.Add(name + "." + key);
                    continue;
                }

                aliasOfferSet = true;
                aliasOffer = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                continue;
            }

            if (!key.StartsWith(NoCardPrefix, StringComparison.Ordinal)
                || !int.TryParse(key.AsSpan(NoCardPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var part)
                || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                if (notRead is null)
                    return "unknown expect key '" + key + "'.";
                notRead.Add(name + "." + key);
                continue;
            }

            noCard[part] = value.GetBoolean();
        }

        checkedStep = step with
        {
            Expect = expect with { Extra = null, NoCard = noCard, AliasOfferSet = aliasOfferSet, AliasOffer = aliasOffer },
        };
        return null;
    }
}
