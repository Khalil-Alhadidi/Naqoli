using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Agent.Core.Masking;
using Agent.Core.Requests;
using Agent.Core.Trace;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent.Core.Parsing;

/// <summary>
/// The model parser (rule 2): one read-only call per message through <see cref="IChatClient"/>
/// (<c>Microsoft.Extensions.AI</c>, so a candidate is a configuration change). It sends the frozen prompt
/// (<c>parse-prompt.md</c>, <see cref="PromptVersion"/>) as the system message and, as the user message, the pending
/// note and the masked text only (rule 5). It asks for the strict structure (<see cref="ParsedRequestsDto"/>) through
/// <see cref="ChatOptions.ResponseFormat"/>, reads the reply strictly and hands it to <see cref="ParsedRequestValidator"/>
/// with the message's mask map, which checks every request of the list; the validated list, in the order spoken, is
/// the <see cref="ParsedMessage"/> it returns (D7). It never throws for a bad reply (that is one
/// <see cref="RequestIntent.Unclear"/> request with a
/// <see cref="ParsedRequest.Problem"/>: the validator's field name, or one of the parser's own problems,
/// <see cref="JsonProblem"/>, <see cref="TruncatedProblem"/> and <see cref="RefusalProblem"/>); a call that fails or outlasts the candidate's timeout throws
/// <see cref="ParserUnavailableException"/>. Logs hold the model id, the time, the tokens (the cache writes and reads
/// apart) and the cost, never the text. The frozen prompt is marked cacheable (<see cref="ModelCandidate.PromptCacheTtl"/>).
/// </summary>
/// <remarks>
/// Structured output: the Anthropic adapter (<c>AnthropicClientExtensions.AsIChatClient</c>, package <c>Anthropic</c>
/// 12.56.0) maps <see cref="ChatResponseFormatJson"/> with a schema to the API's <c>output_config.format</c>
/// (<c>json_schema</c>), transforming the schema to what the API supports. <c>AnthropicAdapterTests</c> proves it offline
/// by reading the request body the adapter sends, so no <see cref="ChatOptions.RawRepresentationFactory"/> fallback is
/// built.
/// </remarks>
public sealed partial class ClaudeParser : IParser, IParserUsage
{
    /// <summary>The <see cref="ParsedRequest.Problem"/> of a reply that is not the structure (not JSON, an unknown field).</summary>
    public const string JsonProblem = "json";

    /// <summary>The <see cref="ParsedRequest.Problem"/> of a reply cut off at the candidate's output limit.</summary>
    public const string TruncatedProblem = "truncated";

    /// <summary>
    /// The <see cref="ParsedRequest.Problem"/> of a reply the model refused to give: the API's stop reason
    /// <c>refusal</c>, which the adapter reports as <see cref="ChatFinishReason.ContentFilter"/>.
    /// </summary>
    public const string RefusalProblem = "refusal";

    /// <summary>The Model-zone trace step when the reply listed more than one request (detail: how many, D7).</summary>
    public const string MoreThanOneRequestStep = "more_than_one_request";

    private const string PromptResource = "Agent.Core.Parsing.parse-prompt.md";
    private const string VersionPrefix = "version: ";
    private const string MessagePrefix = "Message: ";
    private const string AnswerPrefix = "Answer: ";

    /// <summary>The key under which the Anthropic adapter puts the cache writes in <see cref="UsageDetails.AdditionalCounts"/>.</summary>
    private const string CacheCreationProperty = "CacheCreationInputTokens";

    /// <summary>The API's own field name for the cache writes, read from <see cref="UsageDetails.AdditionalCounts"/> too.</summary>
    private const string CacheCreationField = "cache_creation_input_tokens";

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.General)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        // A reply that names a field twice is not the structure (review R1), rather than last-wins.
        AllowDuplicateProperties = false,
    };

    private static readonly Lazy<string> LazyPrompt = new(LoadPrompt);
    private static readonly Lazy<JsonElement> LazySchema = new(BuildSchema);

    private readonly IChatClient client;
    private readonly ModelCandidate candidate;
    private readonly TimeProvider clock;
    private readonly ITraceSink trace;
    private readonly ILogger<ClaudeParser> logger;
    private ParseUsage? lastUsage;

    /// <exception cref="InvalidOperationException">The options name no candidate (the start-up validation says which).</exception>
    public ClaudeParser(IChatClient client, IOptions<ModelOptions> options, TimeProvider clock, ITraceSink trace, ILogger<ClaudeParser> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(logger);

        var model = options.Value;
        candidate = model.Selected
            ?? throw new InvalidOperationException("Model:Candidate must name one of Model:Candidates.");
        Name = "claude:" + model.Candidate;
        this.client = client;
        this.clock = clock;
        this.trace = trace;
        this.logger = logger;
    }

    /// <summary>The frozen prompt (<c>parse-prompt.md</c>), sent as the system message.</summary>
    public static string Prompt => LazyPrompt.Value;

    /// <summary>The prompt's version: its first line, <c>version: N</c>.</summary>
    public static string PromptVersion => Prompt.Split('\n', 2)[0][VersionPrefix.Length..].Trim();

    /// <summary>
    /// The JSON schema of <see cref="ParsedRequestsDto"/> asked for: exported by <see cref="JsonSchemaExporter"/>, every
    /// object closed (<c>additionalProperties: false</c>) with every field required, and each enum string limited to its set.
    /// </summary>
    public static JsonElement Schema => LazySchema.Value;

    public string Name { get; }

    public ParseUsage? LastUsage => Volatile.Read(ref lastUsage);

    /// <summary>The prompt's examples, in order: each message and the answer the prompt gives for it.</summary>
    public static IReadOnlyList<(string Message, string Answer)> Examples()
    {
        var lines = Prompt.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var examples = new List<(string, string)>();
        for (var i = 0; i + 1 < lines.Count; i++)
        {
            if (lines[i].StartsWith(MessagePrefix, StringComparison.Ordinal) && lines[i + 1].StartsWith(AnswerPrefix, StringComparison.Ordinal))
                examples.Add((lines[i][MessagePrefix.Length..], lines[i + 1][AnswerPrefix.Length..]));
        }

        return examples;
    }

    public async Task<ParsedMessage> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);

        // Rule 5 in depth (review M3): a caller that did not mask (no map) cannot send a number to the model.
        var map = context.Map;
        if (map is null)
        {
            var masked = Masker.Mask(text);
            text = masked.Text;
            map = masked.Map;
        }

        // Prompt caching (Khalil, 18 Oct 2026): only the frozen prompt is marked cacheable, at the end of its one block; the
        // user message, which holds the user's text and the pending note, never carries a cache control.
        List<ChatMessage> messages =
        [
            SystemMessage(candidate.PromptCacheTtl),
            new(ChatRole.User, "Pending: " + (context.PendingNote ?? "none") + "\nMessage: " + text),
        ];
        var options = new ChatOptions
        {
            ModelId = candidate.ModelId,
            MaxOutputTokens = candidate.MaxOutputTokens,
            Temperature = candidate.Temperature,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(Schema, "parse", "The requests in the user's message, in the order spoken."),
            // The adapter has no option for output_config.effort (ChatOptions.Reasoning cannot say max, and would also set
            // the thinking mode), so the candidate's effort goes in the SDK's own parameters; the adapter fills in the rest,
            // the JSON-schema format included (AnthropicAdapterTests reads both from the request body).
            RawRepresentationFactory = _ => new MessageCreateParams
            {
                MaxTokens = candidate.MaxOutputTokens,
                Messages = [],
                Model = candidate.ModelId,
                OutputConfig = new OutputConfig { Effort = EffortOf(candidate.Effort) },
            },
        };

        var started = clock.GetTimestamp();
        ChatResponse response;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(candidate.TimeoutSeconds));
            try
            {
                response = await client.GetResponseAsync(messages, options, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Unavailable("timeout", started);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw Unavailable(ReasonOf(ex), started);
            }
        }

        var duration = clock.GetElapsedTime(started);
        var (inputTokens, outputTokens, cacheWriteTokens, cacheReadTokens) = TokensOf(response);
        var cost = candidate.CostUsd(inputTokens, outputTokens, cacheWriteTokens, cacheReadTokens);
        Volatile.Write(
            ref lastUsage,
            new ParseUsage(ToInt(inputTokens), ToInt(outputTokens), ToInt(cacheWriteTokens), ToInt(cacheReadTokens), cost, duration));
        LogParsed(
            candidate.ModelId,
            (long)duration.TotalMilliseconds,
            inputTokens,
            outputTokens,
            cacheWriteTokens,
            cacheReadTokens,
            cost,
            response.FinishReason?.Value ?? "none");

        // Cut off at MaxOutputTokens (thinking counts against it): whatever text came is incomplete.
        if (response.FinishReason == ChatFinishReason.Length)
            return Unclear(TruncatedProblem);

        // The model refused the message (stop reason refusal, reported by the adapter as ContentFilter): no structure.
        if (response.FinishReason == ChatFinishReason.ContentFilter)
        {
            LogRefused(candidate.ModelId, (long)duration.TotalMilliseconds);
            return Unclear(RefusalProblem);
        }

        if (!TryRead(response.Text, out var reply))
            return Unclear(JsonProblem);

        if (reply.Requests is { Count: > 1 } several)
            trace.Record(TraceZone.Model, MoreThanOneRequestStep, several.Count.ToString(CultureInfo.InvariantCulture));

        return new ParsedMessage(ParsedRequestValidator.Validate(reply, map).Requests);
    }

    /// <summary>A reply with no structure to validate: one unclear request carrying the parser's own problem.</summary>
    private static ParsedMessage Unclear(string problem)
        => ParsedMessage.One(new ParsedRequest { Intent = RequestIntent.Unclear, Problem = problem });

    /// <summary>
    /// The system message: the frozen prompt as one text block, marked cacheable with the candidate's lifetime
    /// (<c>5m</c> or <c>1h</c>), or the plain prompt when the lifetime is <c>none</c>.
    /// </summary>
    private static ChatMessage SystemMessage(string promptCacheTtl) => promptCacheTtl switch
    {
        ModelOptions.CacheTtl5m => new ChatMessage(ChatRole.System, [new TextContent(Prompt).WithCacheControl(Ttl.Ttl5m)]),
        ModelOptions.CacheTtl1h => new ChatMessage(ChatRole.System, [new TextContent(Prompt).WithCacheControl(Ttl.Ttl1h)]),
        ModelOptions.CacheTtlNone => new ChatMessage(ChatRole.System, Prompt),
        _ => throw new InvalidOperationException("Unknown prompt cache lifetime; the start-up validation lists the accepted ones."),
    };

    /// <summary>
    /// The call's four token counts: the uncached input (the API's <c>input_tokens</c>), the output, the cache writes
    /// (<c>cache_creation_input_tokens</c>) and the cache reads (<c>cache_read_input_tokens</c>). Read from the SDK's own
    /// usage when the response carries the API's message, else from <see cref="UsageDetails"/>, so a client that does not
    /// pass the message on still reports. <see cref="UsageDetails"/> counts as the Anthropic adapter fills it
    /// (<c>AnthropicAdapterTests</c>): <see cref="UsageDetails.InputTokenCount"/> is the whole input, the reads are
    /// <see cref="UsageDetails.CachedInputTokenCount"/>, the writes are in <see cref="UsageDetails.AdditionalCounts"/>
    /// (the SDK's property name, or the API's field name); the uncached input is what is left of the whole.
    /// </summary>
    private static (long Input, long Output, long CacheWrite, long CacheRead) TokensOf(ChatResponse response)
    {
        if (response.RawRepresentation is Message { Usage: { } usage })
        {
            return (
                usage.InputTokens,
                usage.OutputTokens,
                usage.CacheCreationInputTokens ?? 0,
                usage.CacheReadInputTokens ?? 0);
        }

        var details = response.Usage;
        long cacheWrite = 0;
        if (details?.AdditionalCounts is { } counts
            && (counts.TryGetValue(CacheCreationProperty, out var written) || counts.TryGetValue(CacheCreationField, out written)))
            cacheWrite = Math.Max(0, written);
        var cacheRead = Math.Max(0, details?.CachedInputTokenCount ?? 0);
        var input = Math.Max(0, (details?.InputTokenCount ?? 0) - cacheWrite - cacheRead);
        return (input, details?.OutputTokenCount ?? 0, cacheWrite, cacheRead);
    }

    private static bool TryRead(string? text, [NotNullWhen(true)] out ParsedRequestsDto? reply)
    {
        reply = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        try
        {
            reply = JsonSerializer.Deserialize<ParsedRequestsDto>(text, StrictJson);
            return reply is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The reason of a failed call: <c>http_NNN</c> for an answer with a status (a bad key is 401, a rate limit 429),
    /// <c>connection</c> for a transport failure, otherwise the exception's type. Never its message, which may hold the
    /// response body.
    /// </summary>
    private static string ReasonOf(Exception ex) => ex switch
    {
        AnthropicApiException api => "http_" + ((int)api.StatusCode).ToString(CultureInfo.InvariantCulture),
        HttpRequestException { StatusCode: { } status } => "http_" + ((int)status).ToString(CultureInfo.InvariantCulture),
        HttpRequestException or AnthropicIOException => "connection",
        _ => ex.GetType().Name,
    };

    private ParserUnavailableException Unavailable(string reason, long started)
    {
        LogUnavailable(candidate.ModelId, reason, (long)clock.GetElapsedTime(started).TotalMilliseconds);
        return new ParserUnavailableException("The model did not answer (" + reason + ").");
    }

    private static Effort EffortOf(string effort) => effort switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => throw new InvalidOperationException("Unknown effort level; the start-up validation lists the accepted ones."),
    };

    private static int ToInt(long tokens) => tokens > int.MaxValue ? int.MaxValue : (int)Math.Max(0, tokens);

    private static string LoadPrompt()
    {
        using var stream = typeof(ClaudeParser).Assembly.GetManifestResourceStream(PromptResource)
            ?? throw new InvalidOperationException($"Embedded resource {PromptResource} is missing.");
        using var reader = new StreamReader(stream);
        var prompt = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!prompt.StartsWith(VersionPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Embedded resource {PromptResource} must start with '{VersionPrefix}'.");
        return prompt;
    }

    private static JsonElement BuildSchema()
    {
        var exporter = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = static (context, node) =>
            {
                if (node is not JsonObject schema)
                    return node;

                if (context.PropertyInfo is null && (context.TypeInfo.Type == typeof(ParsedRequestsDto) || context.TypeInfo.Type == typeof(ParsedRequestDto)))
                    Close(schema);
                else if (context.PropertyInfo?.Name == "requests")
                    schema["type"] = "array";
                else if (context.PropertyInfo?.Name is { } name && EnumOf(name) is { } values)
                {
                    var set = new JsonArray([.. values.Where(v => v is not null).Select(v => (JsonNode?)v)]);
                    if (values.Contains(null))
                    {
                        // The structured-output validator rejects an enum under a type list (["string", "null"]) with
                        // HTTP 400 "Enum value ... does not match declared type". A nullable set is two alternatives.
                        schema.Remove("type");
                        schema["anyOf"] = new JsonArray(
                            new JsonObject { ["type"] = "string", ["enum"] = set },
                            new JsonObject { ["type"] = "null" });
                    }
                    else
                    {
                        schema["type"] = "string";
                        schema["enum"] = set;
                    }
                }

                return schema;
            },
        };

        var node = StrictJson.GetJsonSchemaAsNode(typeof(ParsedRequestsDto), exporter);
        return JsonSerializer.SerializeToElement(node, StrictJson);
    }

    /// <summary>An object schema: non-null, closed, every property required.</summary>
    private static void Close(JsonObject schema)
    {
        schema["type"] = "object";
        schema["additionalProperties"] = false;
        if (schema["properties"] is JsonObject properties)
            schema["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)p.Key)]);
    }

    /// <summary>The fixed set of a field, with null for the fields that may be null.</summary>
    private static string?[]? EnumOf(string field) => field switch
    {
        "intent" => ["transfer", "bill", "purchase", "balance", "unsupported", "unclear"],
        "amount_unit" => ["none", "dinars", "thousand", "million"],
        "biller_code" => ["electricity", "water", "internet", "school_fees", null],
        "product_type" => ["credit", "internet", null],
        "unsupported_reason" => [.. ParsedRequestValidator.UnsupportedReasons, null],
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Parse call to {ModelId}: {DurationMs} ms, {InputTokens} tokens in, {OutputTokens} out, cache {CacheWriteTokens} written and {CacheReadTokens} read, {CostUsd} USD, finish {FinishReason}")]
    private partial void LogParsed(
        string modelId, long durationMs, long inputTokens, long outputTokens, long cacheWriteTokens, long cacheReadTokens, decimal costUsd, string finishReason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Parse call to {ModelId} unavailable ({Reason}) after {DurationMs} ms")]
    private partial void LogUnavailable(string modelId, string reason, long durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Parse call to {ModelId} refused the message after {DurationMs} ms")]
    private partial void LogRefused(string modelId, long durationMs);
}
