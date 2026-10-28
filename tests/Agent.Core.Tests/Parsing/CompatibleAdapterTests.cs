using System.Net;
using System.Text;
using System.Text.Json;
using Agent.Core.Masking;
using Agent.Core.Parsing;
using Agent.Core.Requests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// The parser over the real OpenAI adapter (<see cref="ParserFactory.CreateCompatibleClient"/>), the twin of
/// <see cref="AnthropicAdapterTests"/>, with the HTTP answered by a stub: it proves offline what a <c>compatible</c>
/// candidate's call becomes on the wire (strict <c>json_schema</c>, or <c>json_object</c> with the schema block; the plain
/// prompt; <c>max_completion_tokens</c>; no temperature, cache control or effort), how the reply and its usage are read,
/// and that an HTTP failure becomes <see cref="ParserUnavailableException"/> without the key or the vendor's body.
/// </summary>
public class CompatibleAdapterTests
{
    /// <summary>A test value shaped like a key, not a key.</summary>
    private const string FakeKey = "sk-or-test-not-a-real-key-0000";

    private const string Item = """{"intent":"bill","recipient_text":null,"amount_value":null,"amount_unit":"none","biller_code":"water","bill_label_text":null,"operator_text":null,"product_type":null,"product_value":null,"unsupported_reason":null,"currency_text":null}""";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Reply => "{\"requests\":[" + Item + "]}";

    private static (ClaudeParser Parser, RecordingHandler Handler) Start(string outputMode = ModelOptions.OutputModeJsonSchema)
    {
        var handler = new RecordingHandler();
        var options = ClaudeParserTests.OptionsCompatible(outputMode);
        var parser = new ClaudeParser(
            ParserFactory.CreateCompatibleClient(FakeKey, options.Selected!, new HttpClient(handler)),
            Microsoft.Extensions.Options.Options.Create(options),
            new ParsePacer(TimeProvider.System),
            TimeProvider.System,
            new ClaudeParserTests.RecordingTrace(),
            NullLogger<ClaudeParser>.Instance);
        return (parser, handler);
    }

    /// <summary>A chat completion whose message content is <paramref name="content"/>, with the usage of a cached call.</summary>
    private static HttpResponseMessage Completion(string? content, string finishReason = "stop")
        => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            created = 1_761_609_600,
            model = "gemini-test",
            choices = new[]
            {
                new { index = 0, message = new { role = "assistant", content }, finish_reason = finishReason },
            },
            usage = new
            {
                prompt_tokens = 1200,
                completion_tokens = 40,
                total_tokens = 1240,
                prompt_tokens_details = new { cached_tokens = 1000 },
            },
        }));

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>The text of a message's content, whether sent as one string or as text parts (joined in order).</summary>
    private static string TextOf(JsonElement message)
    {
        var content = message.GetProperty("content");
        return content.ValueKind == JsonValueKind.String
            ? content.GetString()!
            : string.Concat(content.EnumerateArray().Select(part => part.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task TheRequestIsStrictJsonSchemaWithThePromptAlone()
    {
        var (parser, handler) = Start();
        handler.Answer = () => Completion(Reply);

        await parser.ParseAsync("pay the water", new ParseContext("open_question which_bill", MaskMap.Empty), Token);

        var body = Assert.Single(handler.Bodies);
        Assert.Equal("gemini-test", body.GetProperty("model").GetString());
        var system = body.GetProperty("messages")[0];
        Assert.Equal("system", system.GetProperty("role").GetString());
        Assert.Equal(ClaudeParser.Prompt, TextOf(system));

        var format = body.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var jsonSchema = format.GetProperty("json_schema");
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());
        Assert.False(jsonSchema.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());

        Assert.Equal(1500, body.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(body.TryGetProperty("temperature", out _));
        var raw = body.GetRawText();
        Assert.DoesNotContain("cache_control", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("output_config", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonObjectModeSendsTheSchemaBlockAndJsonObject()
    {
        var (parser, handler) = Start(ModelOptions.OutputModeJsonObject);
        handler.Answer = () => Completion(Reply);

        await parser.ParseAsync("pay the water", new ParseContext(null, MaskMap.Empty), Token);

        var body = Assert.Single(handler.Bodies);
        Assert.Equal("json_object", body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(ClaudeParser.Prompt + ClaudeParser.SchemaBlock, TextOf(body.GetProperty("messages")[0]));
    }

    [Fact]
    public async Task TheReplyAndItsUsageAreRead()
    {
        var (parser, handler) = Start();
        handler.Answer = () => Completion(Reply);

        var parsed = (await parser.ParseAsync("pay the water", new ParseContext(null, MaskMap.Empty), Token)).First;

        Assert.Equal(ParsedRequest.Bill("water", null, null, AmountUnit.None), parsed);
        var usage = Assert.IsType<ParseUsage>(parser.LastUsage);
        Assert.Equal((200, 40, 0, 1000), (usage.InputTokens, usage.OutputTokens, usage.CacheWriteTokens, usage.CacheReadTokens));
    }

    [Theory]
    [InlineData("""{"requests":[{"intent":"bill","extra":1}]}""")]
    [InlineData("""{"ok":true}""")]
    public async Task AnOffSchemaJsonObjectReplyIsTheJsonProblem(string content)
    {
        var (parser, handler) = Start(ModelOptions.OutputModeJsonObject);
        handler.Answer = () => Completion(content);

        var parsed = (await parser.ParseAsync("pay the water", new ParseContext(null, MaskMap.Empty), Token)).First;

        Assert.Equal(ClaudeParser.JsonProblem, parsed.Problem);
    }

    [Fact]
    public async Task ALengthStopIsTruncated()
    {
        var (parser, handler) = Start();
        handler.Answer = () => Completion("{\"requests\":[", "length");

        var parsed = (await parser.ParseAsync("pay the water", new ParseContext(null, MaskMap.Empty), Token)).First;

        Assert.Equal(ClaudeParser.TruncatedProblem, parsed.Problem);
    }

    [Fact]
    public async Task AContentFilterStopIsTheRefusal()
    {
        var (parser, handler) = Start();
        handler.Answer = () => Completion(null, "content_filter");

        var parsed = (await parser.ParseAsync("pay the water", new ParseContext(null, MaskMap.Empty), Token)).First;

        Assert.Equal(ClaudeParser.RefusalProblem, parsed.Problem);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "The model did not answer (http_400).")]
    [InlineData(HttpStatusCode.TooManyRequests, "The model did not answer (http_429).")]
    public async Task AnHttpFailureIsUnavailableWithoutTheKeyOrTheBody(HttpStatusCode status, string expected)
    {
        const string VendorText = "max_completion_tokens is not supported";
        var (parser, handler) = Start();
        handler.Answer = () => Json(status, """{"error":{"message":"max_completion_tokens is not supported (echo sk-or-test-not-a-real-key-0000)","type":"invalid_request_error"}}""");

        var ex = await Assert.ThrowsAsync<ParserUnavailableException>(() => parser.ParseAsync("x", new ParseContext(null, MaskMap.Empty), Token));

        Assert.Equal(expected, ex.Message);
        Assert.DoesNotContain(FakeKey, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(VendorText, ex.ToString(), StringComparison.Ordinal);
        // The key went where it should, and only there: one request, retried never.
        Assert.Equal("Bearer " + FakeKey, Assert.Single(handler.Authorizations));
    }

    [Fact]
    public async Task TheOutboundTextIsMasked()
    {
        const string Raw = "send 50000 to 07701234567";
        var (parser, handler) = Start();
        handler.Answer = () => Completion(Reply);

        // No map given: the parser masks the text itself before it leaves (rule 5).
        await parser.ParseAsync(Raw, new ParseContext(null), Token);

        var body = Assert.Single(handler.Bodies);
        Assert.DoesNotContain("07701234567", body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("Pending: none\nMessage: " + Masker.Mask(Raw).Text, TextOf(body.GetProperty("messages")[1]));
    }
}
