using System.Net;
using System.Text;
using System.Text.Json;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The Claude parser over the real Anthropic adapter (<c>AnthropicClient.AsIChatClient</c>), with the HTTP answered by a
/// stub: it proves offline what the parser's <see cref="ChatOptions"/> become on the wire (structured output in
/// <c>output_config.format</c>, <c>max_tokens</c>, no <c>temperature</c>), how the reply and its usage are read, and that
/// an HTTP failure becomes <see cref="ParserUnavailableException"/> without the key.
/// </summary>
public class AnthropicAdapterTests
{
    /// <summary>A test value shaped like a key, not a key.</summary>
    private const string FakeKey = "sk-ant-test-not-a-real-key-0000";

    private const string Item = """{"intent":"bill","recipient_text":null,"amount_value":null,"amount_unit":"none","biller_code":"water","bill_label_text":null,"operator_text":null,"product_type":null,"product_value":null,"unsupported_reason":null,"currency_text":null}""";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (ClaudeParser Parser, StubHandler Handler) Start(string promptCacheTtl = ModelOptions.CacheTtl5m)
    {
        var handler = new StubHandler();
        var client = new AnthropicClient
        {
            ApiKey = FakeKey,
            HttpClient = new HttpClient(handler),
            MaxRetries = 0,
        };
        var options = ClaudeParserTests.Options(promptCacheTtl: promptCacheTtl);
        var parser = new ClaudeParser(
            ParserFactory.CreateChatClient(client, options.Candidates["haiku"]),
            Microsoft.Extensions.Options.Options.Create(options),
            TimeProvider.System,
            new ClaudeParserTests.RecordingTrace(),
            NullLogger<ClaudeParser>.Instance);
        return (parser, handler);
    }

    /// <summary>
    /// A reply with <paramref name="text"/>; its usage carries the two cache counts as the API sends them (null when
    /// the call touched no cache).
    /// </summary>
    private static HttpResponseMessage Message(
        string text, int inputTokens = 1200, int outputTokens = 80, int? cacheWriteTokens = null, int? cacheReadTokens = null)
        => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "msg_test",
            type = "message",
            role = "assistant",
            model = "claude-haiku-5-5",
            content = new[] { new { type = "text", text } },
            stop_reason = "end_turn",
            stop_sequence = (string?)null,
            usage = new
            {
                input_tokens = inputTokens,
                output_tokens = outputTokens,
                cache_creation_input_tokens = cacheWriteTokens,
                cache_read_input_tokens = cacheReadTokens,
            },
        }));

    private static string Reply => "{" + Quoted("requests") + ":[" + Item + "]}";

    private static string Quoted(string name) => JsonSerializer.Serialize(name);

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task TheStructuredOutputReachesTheWireAsOutputConfigFormat()
    {
        var (parser, handler) = Start();
        handler.Answer = () => Message("{\"requests\":[" + Item + "]}");

        var parsed = (await parser.ParseAsync("pay the water", new ParseContext("open_question which_bill", MaskMap.Empty), Token)).First;

        Assert.Equal(ParsedRequest.Bill("water", null, null, AmountUnit.None), parsed);
        var body = Assert.Single(handler.Bodies);
        Assert.Equal("claude-haiku-5-5", body.GetProperty("model").GetString());
        Assert.Equal(1500, body.GetProperty("max_tokens").GetInt32());
        Assert.False(body.TryGetProperty("temperature", out _));

        // The adapter maps ChatOptions.ResponseFormat (a JSON schema) to Anthropic structured outputs.
        // The candidate's effort and the structured output share output_config.
        Assert.Equal("low", body.GetProperty("output_config").GetProperty("effort").GetString());
        var format = body.GetProperty("output_config").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var schema = format.GetProperty("schema");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.True(schema.GetProperty("properties").TryGetProperty("requests", out _));

        var bodyText = body.GetRawText();
        Assert.Contains("open_question which_bill", bodyText, StringComparison.Ordinal);
        Assert.Contains("pay the water", bodyText, StringComparison.Ordinal);

        var usage = Assert.IsType<ParseUsage>(parser.LastUsage);
        Assert.Equal((1200, 80, 0, 0), (usage.InputTokens, usage.OutputTokens, usage.CacheWriteTokens, usage.CacheReadTokens));
    }

    [Theory]
    [InlineData(ModelOptions.CacheTtl5m)]
    [InlineData(ModelOptions.CacheTtl1h)]
    public async Task TheSystemPromptIsOneTextBlockMarkedCacheableAndTheUserMessageIsNot(string ttl)
    {
        var (parser, handler) = Start(ttl);
        handler.Answer = () => Message(Reply);

        await parser.ParseAsync("pay the water", new ParseContext("open_question which_bill", MaskMap.Empty), Token);

        var body = Assert.Single(handler.Bodies);
        var block = Assert.Single(body.GetProperty("system").EnumerateArray());
        Assert.Equal("text", block.GetProperty("type").GetString());
        Assert.Equal(ClaudeParser.Prompt, block.GetProperty("text").GetString());
        var cacheControl = block.GetProperty("cache_control");
        Assert.Equal("ephemeral", cacheControl.GetProperty("type").GetString());
        Assert.Equal(ttl, cacheControl.GetProperty("ttl").GetString());

        // The user's text and the pending note are never cached.
        var messages = body.GetProperty("messages");
        Assert.Contains("pay the water", messages.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("cache_control", messages.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AUsageWithoutTheCacheFieldsCountsNoCache()
    {
        // An older answer shape: the usage has no cache fields at all.
        var (parser, handler) = Start();
        handler.Answer = () => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "msg_test",
            type = "message",
            role = "assistant",
            model = "claude-haiku-5-5",
            content = new[] { new { type = "text", text = Reply } },
            stop_reason = "end_turn",
            stop_sequence = (string?)null,
            usage = new { input_tokens = 1200, output_tokens = 80 },
        }));

        await parser.ParseAsync("pay the water", new ParseContext(null, MaskMap.Empty), Token);

        var usage = Assert.IsType<ParseUsage>(parser.LastUsage);
        Assert.Equal((1200, 80, 0, 0), (usage.InputTokens, usage.OutputTokens, usage.CacheWriteTokens, usage.CacheReadTokens));
    }

    [Fact]
    public async Task NoneSendsNoCacheControl()
    {
        var (parser, handler) = Start(ModelOptions.CacheTtlNone);
        handler.Answer = () => Message(Reply);

        await parser.ParseAsync("pay the water", new ParseContext(null, MaskMap.Empty), Token);

        var body = Assert.Single(handler.Bodies);
        Assert.DoesNotContain("cache_control", body.GetRawText(), StringComparison.Ordinal);
        var system = body.GetProperty("system");
        var prompt = system.ValueKind == JsonValueKind.String ? system.GetString() : Assert.Single(system.EnumerateArray()).GetProperty("text").GetString();
        Assert.Equal(ClaudeParser.Prompt, prompt);
    }

    [Fact]
    public async Task TheCacheCountsAreReadAndPricedFromAllFour()
    {
        var (parser, handler) = Start();
        handler.Answer = () => Message(Reply, inputTokens: 200, outputTokens: 80, cacheWriteTokens: 1_000, cacheReadTokens: 3_000);

        await parser.ParseAsync("pay the water", new ParseContext(null, MaskMap.Empty), Token);

        var usage = Assert.IsType<ParseUsage>(parser.LastUsage);
        Assert.Equal((200, 80, 1_000, 3_000), (usage.InputTokens, usage.OutputTokens, usage.CacheWriteTokens, usage.CacheReadTokens));
        // 200 x 0.10 + 80 x 0.50 + 1,000 x 0.125 + 3,000 x 0.01, per million.
        Assert.Equal(0.000215m, usage.CostUsd);
    }

    [Fact]
    public async Task TheAdaptersUsageDetailsForACachedCall()
    {
        // What the adapter itself reports in UsageDetails for a call that wrote and read the cache, read without the
        // parser: the parser prefers the SDK's message, so this pins the fallback's assumptions about the adapter.
        var handler = new StubHandler { Answer = () => Message(Reply, inputTokens: 200, outputTokens: 80, cacheWriteTokens: 1_000, cacheReadTokens: 3_000) };
        var client = new AnthropicClient { ApiKey = FakeKey, HttpClient = new HttpClient(handler), MaxRetries = 0 };
        var chat = ParserFactory.CreateChatClient(client, ClaudeParserTests.Options().Candidates["haiku"]);

        var response = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], new ChatOptions { ModelId = "claude-haiku-5-5", MaxOutputTokens = 100 }, Token);

        // Found with this stub (Anthropic 12.56.0): the adapter keeps the SDK's message as the raw representation, and its
        // InputTokenCount is the whole input (input_tokens + cache writes + cache reads), the reads are
        // CachedInputTokenCount, and the writes are in AdditionalCounts under the SDK's property name.
        Assert.IsType<Anthropic.Models.Messages.Message>(response.RawRepresentation);
        var usage = Assert.IsType<UsageDetails>(response.Usage);
        Assert.Equal(200 + 1_000 + 3_000, usage.InputTokenCount);
        Assert.Equal(80, usage.OutputTokenCount);
        Assert.Equal(3_000, usage.CachedInputTokenCount);
        Assert.Equal(1_000, Assert.Single(usage.AdditionalCounts!, kv => kv.Key == "CacheCreationInputTokens").Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "401")]
    [InlineData(HttpStatusCode.BadRequest, "400")]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData((HttpStatusCode)529, "529")]
    public async Task AnHttpFailureIsUnavailableWithTheStatusAndWithoutTheKey(HttpStatusCode status, string expected)
    {
        var (parser, handler) = Start();
        handler.Answer = () => Json(status, """{"type":"error","error":{"type":"some_error","message":"echo sk-ant-test-not-a-real-key-0000"}}""");

        var ex = await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConnectionFailureIsUnavailable()
    {
        var (parser, handler) = Start();
        handler.Answer = () => throw new HttpRequestException("no route");

        await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token));
    }

    /// <summary>Records each request body and answers with <see cref="Answer"/>.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<JsonElement> Bodies { get; } = [];

        public Func<HttpResponseMessage> Answer { get; set; } = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                Bodies.Add(JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
            return Answer();
        }
    }
}
