using System.Collections.Concurrent;
using System.Text.Json;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Agent.Core.Trace;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The Claude parser over a fake <see cref="IChatClient"/>: what it sends (the frozen prompt, the masked text and the
/// pending note, the structured-output schema and the limits), how it reads the reply (strictly, then through the
/// structure validator), what a call cost, and how every failure becomes <see cref="ParserUnavailableException"/>.
/// </summary>
public class ClaudeParserTests
{
    private const string Transfer = """{"requests":[{"intent":"transfer","recipient_text":"Ali","amount_value":50,"amount_unit":"thousand","biller_code":null,"bill_label_text":null,"operator_text":null,"product_type":null,"product_value":null,"unsupported_reason":null,"currency_text":null}]}""";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static ModelOptions Options(float? temperature = null, int timeoutSeconds = 20) => new()
    {
        Candidate = "haiku",
        Candidates =
        {
            ["haiku"] = new ModelCandidate
            {
                Provider = "anthropic",
                ModelId = "claude-haiku-5-5",
                MaxOutputTokens = 1500,
                Effort = "low",
                Temperature = temperature,
                TimeoutSeconds = timeoutSeconds,
                InputPricePerMTok = 0.10m,
                OutputPricePerMTok = 0.50m,
            },
        },
    };

    private static (ClaudeParser Parser, FakeChatClient Client, RecordingTrace Trace) Start(ModelOptions? options = null)
    {
        var client = new FakeChatClient();
        var trace = new RecordingTrace();
        var parser = new ClaudeParser(
            client, Microsoft.Extensions.Options.Options.Create(options ?? Options()), TimeProvider.System, trace, NullLogger<ClaudeParser>.Instance);
        return (parser, client, trace);
    }

    private static string Dto(string fields)
        => "{\"requests\":[{" + fields + "}]}";

    [Fact]
    public async Task AValidReplyIsReadThroughTheValidator()
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(Transfer));

        var parsed = await parser.ParseAsync("send fifty to Ali", new ParseContext(null, MaskMap.Empty), Token);

        Assert.Equal(ParsedRequest.Transfer("Ali", 50m, AmountUnit.Thousand), parsed);
    }

    [Fact]
    public async Task ItSendsTheFrozenPromptAndTheMaskedTextWithThePendingNote()
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(Transfer));

        await parser.ParseAsync("send 50000 to <NUM_1>", new ParseContext("pending_card transfer", Masker.Mask("send 50000 to 07701234567").Map), Token);
        await parser.ParseAsync("balance", new ParseContext(null, MaskMap.Empty), Token);

        var (first, _) = client.Calls[0];
        Assert.Equal([ChatRole.System, ChatRole.User], first.Select(m => m.Role));
        Assert.Equal(ClaudeParser.Prompt, first[0].Text);
        Assert.Equal("Pending: pending_card transfer\nMessage: send 50000 to <NUM_1>", first[1].Text);
        Assert.Equal("Pending: none\nMessage: balance", client.Calls[1].Messages[1].Text);
    }

    [Fact]
    public async Task ItAsksForTheStrictStructureWithinTheLimits()
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(Transfer));

        await parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token);

        var options = Assert.IsType<ChatOptions>(client.Calls[0].Options);
        Assert.Equal("claude-haiku-5-5", options.ModelId);
        Assert.Equal(1500, options.MaxOutputTokens);
        Assert.NotNull(options.RawRepresentationFactory);
        // Not configured: no temperature is sent (Claude 5.5 models reject a non-default one).
        Assert.Null(options.Temperature);
        var format = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        Assert.Equal("parse", format.SchemaName);
        Assert.NotNull(format.Schema);
        Assert.Equal(ClaudeParser.Schema.GetRawText(), format.Schema.Value.GetRawText());
    }

    [Fact]
    public async Task AConfiguredTemperatureIsSent()
    {
        var (parser, client, _) = Start(Options(temperature: 0f));
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(Transfer));

        await parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token);

        Assert.Equal(0f, client.Calls[0].Options?.Temperature);
    }

    [Fact]
    public void TheSchemaIsClosedAndNamesEverySetValue()
    {
        var schema = ClaudeParser.Schema;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["requests"], schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!));

        var item = schema.GetProperty("properties").GetProperty("requests").GetProperty("items");
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        var required = item.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        string[] fields =
        [
            "intent", "recipient_text", "amount_value", "amount_unit", "biller_code", "bill_label_text", "operator_text",
            "product_type", "product_value", "unsupported_reason", "currency_text",
        ];
        Assert.Equal(fields.ToHashSet(), required);

        var properties = item.GetProperty("properties");
        string?[] Enum(string name)
        {
            var field = properties.GetProperty(name);
            if (field.TryGetProperty("enum", out var set))
            {
                Assert.Equal("string", field.GetProperty("type").GetString());
                return [.. set.EnumerateArray().Select(e => e.GetString())];
            }

            // A nullable set is two alternatives. The structured-output validator rejects an enum under a type list
            // (["string", "null"]): "Enum value 'electricity' does not match declared type" (HTTP 400, 15 Oct 2026).
            var alternatives = field.GetProperty("anyOf").EnumerateArray().ToArray();
            Assert.Equal(2, alternatives.Length);
            Assert.Equal("string", alternatives[0].GetProperty("type").GetString());
            Assert.Equal("null", alternatives[1].GetProperty("type").GetString());
            Assert.False(alternatives[1].TryGetProperty("enum", out _));
            return [.. alternatives[0].GetProperty("enum").EnumerateArray().Select(e => e.GetString()), null];
        }

        Assert.Equal(["transfer", "bill", "purchase", "balance", "unsupported", "unclear"], Enum("intent"));
        Assert.Equal(["none", "dinars", "thousand", "million"], Enum("amount_unit"));
        Assert.Equal(["electricity", "water", "internet", "school_fees", null], Enum("biller_code"));
        Assert.Equal(["credit", "internet", null], Enum("product_type"));
        Assert.Equal([.. ParsedRequestValidator.UnsupportedReasons, null], Enum("unsupported_reason"));
    }

    [Fact]
    public void NoEnumOfTheSchemaSitsUnderATypeList()
    {
        // The structured-output validator rejects an enum whose node declares a type list; a nullable set is an anyOf
        // of a string enum and a null node. A plain nullable field (no enum) may keep its type list.
        static void Check(JsonElement node, string path)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("enum", out _))
                    Assert.True(node.GetProperty("type").ValueKind == JsonValueKind.String, path + ": enum under a type list");
                foreach (var property in node.EnumerateObject())
                    Check(property.Value, path + "." + property.Name);
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var element in node.EnumerateArray())
                    Check(element, path + "[" + i++ + "]");
            }
        }

        Check(ClaudeParser.Schema, "schema");
    }

    [Fact]
    public async Task UsageIsMappedToTokensAndCost()
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(Transfer, inputTokens: 1_500, outputTokens: 150));

        Assert.Null(parser.LastUsage);
        await parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token);

        var usage = Assert.IsType<ParseUsage>(parser.LastUsage);
        Assert.Equal((1_500, 150), (usage.InputTokens, usage.OutputTokens));
        // 1,500 x 0.10 / 1M + 150 x 0.50 / 1M.
        Assert.Equal(0.000225m, usage.CostUsd);
        Assert.True(usage.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ATokenOfThisMessageReachesTheConversationAsAToken()
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(Dto(
            "\"intent\":\"transfer\",\"recipient_text\":\"<NUM_1>\",\"amount_value\":50000,\"amount_unit\":\"none\",\"biller_code\":null,\"bill_label_text\":null,\"operator_text\":null,\"product_type\":null,\"product_value\":null,\"unsupported_reason\":null,\"currency_text\":null")));

        var withMap = await parser.ParseAsync("send 50000 to <NUM_1>", new ParseContext(null, Masker.Mask("send 50000 to 07701234567").Map), Token);
        var withoutMap = await parser.ParseAsync("send 50000 to <NUM_1>", new ParseContext(null), Token);

        Assert.Equal("<NUM_1>", withMap.RecipientText);
        Assert.Equal((RequestIntent.Unclear, "recipient_text"), (withoutMap.Intent, withoutMap.Problem));
    }

    [Fact]
    public async Task WithoutAMapTheParserMasksTheTextItself()
    {
        // Review M3: a caller that did not mask cannot send a number to the model.
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(Dto(
            "\"intent\":\"transfer\",\"recipient_text\":\"<NUM_1>\",\"amount_value\":50000,\"amount_unit\":\"none\",\"biller_code\":null,\"bill_label_text\":null,\"operator_text\":null,\"product_type\":null,\"product_value\":null,\"unsupported_reason\":null,\"currency_text\":null")));

        var parsed = await parser.ParseAsync("send 50000 to 07701234567", new ParseContext(null), Token);

        Assert.Equal("Pending: none\nMessage: send 50000 to <NUM_1>", client.Calls[0].Messages[1].Text);
        Assert.Equal("<NUM_1>", parsed.RecipientText);
    }

    [Fact]
    public async Task ARepeatedFieldIsNotTheStructure()
    {
        // Review R1: a reply that names intent twice is refused, not read last-wins.
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(Transfer.Replace("\"intent\":\"transfer\"", "\"intent\":\"balance\",\"intent\":\"transfer\"", StringComparison.Ordinal)));

        var parsed = await parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token);

        Assert.Equal((RequestIntent.Unclear, ClaudeParser.JsonProblem), (parsed.Intent, parsed.Problem));
    }

    [Fact]
    public async Task AReplyOutsideTheSetsIsUnclearWithTheField()
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(Transfer.Replace("\"transfer\"", "\"execute\"", StringComparison.Ordinal)));

        var parsed = await parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token);

        Assert.Equal((RequestIntent.Unclear, "intent"), (parsed.Intent, parsed.Problem));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"requests":[{"intent":"transfer"}],"execute":true}""")]
    [InlineData("""{"requests":[{"intent":"transfer","run":"now"}]}""")]
    [InlineData("null")]
    [InlineData("")]
    public async Task AReplyThatIsNotTheStructureIsUnclear(string text)
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json(text, 10, 5));

        var parsed = await parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token);

        Assert.Equal((RequestIntent.Unclear, ClaudeParser.JsonProblem), (parsed.Intent, parsed.Problem));
        Assert.Equal(10, parser.LastUsage?.InputTokens);
    }

    [Fact]
    public async Task AReplyCutOffAtTheOutputLimitIsUnclearAsTruncated()
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) =>
        {
            var response = FakeChatClient.Json(Transfer[..40], 1_000, 1_500);
            response.FinishReason = ChatFinishReason.Length;
            return Task.FromResult(response);
        };

        var parsed = await parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token);

        Assert.Equal((RequestIntent.Unclear, ClaudeParser.TruncatedProblem), (parsed.Intent, parsed.Problem));
        Assert.Equal(1_500, parser.LastUsage?.OutputTokens);
    }

    [Fact]
    public async Task MoreThanOneRequestTakesTheFirstAndTracesTheCount()
    {
        var (parser, client, trace) = Start();
        var item = Transfer[13..^2];
        client.Reply = (_, _, _) => Task.FromResult(FakeChatClient.Json("{\"requests\":[" + item + "," + item.Replace("Ali", "Omar", StringComparison.Ordinal) + "]}"));

        var parsed = await parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token);

        Assert.Equal("Ali", parsed.RecipientText);
        Assert.Contains((TraceZone.Model, ClaudeParser.MoreThanOneRequestStep, "2"), trace.Entries);
    }

    [Fact]
    public async Task ATimeoutIsUnavailable()
    {
        var (parser, client, _) = Start(Options(timeoutSeconds: 1));
        client.Reply = async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return FakeChatClient.Json(Transfer);
        };

        var ex = await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token));

        Assert.Contains("timeout", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConnectionFailureIsUnavailable()
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => throw new HttpRequestException("no route");

        var ex = await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token));

        Assert.Contains("connection", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnyOtherFailureIsUnavailable()
    {
        var (parser, client, _) = Start();
        client.Reply = (_, _, _) => throw new InvalidOperationException("send fifty to 07701234567");

        var ex = await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token));

        // The inner exception's message (which a library might fill with the input) is not repeated.
        Assert.DoesNotContain("0770", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCallersCancellationPropagates()
    {
        var (parser, client, _) = Start();
        client.Reply = async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return FakeChatClient.Json(Transfer);
        };
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), cancelled.Token));
    }

    [Fact]
    public void TheNameCarriesTheCandidateAndThePromptItsVersion()
    {
        var (parser, _, _) = Start();

        Assert.Equal("claude:haiku", parser.Name);
        Assert.Equal("1", ClaudeParser.PromptVersion);
        Assert.StartsWith("version: 1", ClaudeParser.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCandidateIsRefusedAtConstruction()
    {
        var options = Options();
        options.Candidate = "gpt";

        Assert.Throws<InvalidOperationException>(() => new ClaudeParser(
            new FakeChatClient(), Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System, new RecordingTrace(), NullLogger<ClaudeParser>.Instance));
    }

    /// <summary>A trace sink that keeps every step.</summary>
    internal sealed class RecordingTrace : ITraceSink
    {
        private readonly ConcurrentQueue<(TraceZone Zone, string Step, string? Detail)> entries = new();

        public IReadOnlyList<(TraceZone Zone, string Step, string? Detail)> Entries => [.. entries];

        public void Record(TraceZone zone, string step, string? detail) => entries.Enqueue((zone, step, detail));
    }
}
