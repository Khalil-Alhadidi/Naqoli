using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

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
/// fields present, nulls only where the format allows them, and the step forms and fault codes of the README. Any problem
/// is one <see cref="CaseFileException"/> naming the line and the case id; the message never quotes case text.
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
    };

    /// <summary>Every case of the file at <paramref name="path"/>, in file order.</summary>
    /// <exception cref="CaseFileException">The file is missing, a line is not a valid case, or two cases share an id.</exception>
    public static IReadOnlyList<EvalCase> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new CaseFileException($"{Path.GetFileName(path)}: the case file does not exist.");

        var cases = new List<EvalCase>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var id = TryReadId(line);
            EvalCase? read;
            try
            {
                read = JsonSerializer.Deserialize<EvalCase>(line, Json);
            }
            catch (JsonException ex)
            {
                // The serializer's message names the JSON path and the member, never the value.
                throw new CaseFileException(path, number, id, "not a valid case: " + ex.Message, ex);
            }

            if (read is null)
                throw new CaseFileException(path, number, id, "the line is null.");

            var problem = Validate(read, out var checkedCase);
            if (problem is not null)
                throw new CaseFileException(path, number, read.Id, problem);
            if (!ids.Add(checkedCase.Id))
                throw new CaseFileException(path, number, checkedCase.Id, "the id is used twice.");
            cases.Add(checkedCase);
        }

        return cases;
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

    /// <summary>The first problem of <paramref name="evalCase"/>, or null; <paramref name="checkedCase"/> has the <c>no_card_N</c> keys parsed.</summary>
    private static string? Validate(EvalCase evalCase, out EvalCase checkedCase)
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
            var problem = ValidateStep(step, out var checkedStep);
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

    private static string? ValidateStep(CaseStep step, out CaseStep checkedStep)
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
                    return "alias_offer is a text or null.";
                aliasOfferSet = true;
                aliasOffer = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                continue;
            }

            if (!key.StartsWith(NoCardPrefix, StringComparison.Ordinal)
                || !int.TryParse(key.AsSpan(NoCardPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var part)
                || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return "unknown expect key '" + key + "'.";
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
