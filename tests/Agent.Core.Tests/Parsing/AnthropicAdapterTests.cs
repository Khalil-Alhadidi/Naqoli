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

    private static (ClaudeParser Parser, StubHandler Handler) Start()
    {
        var handler = new StubHandler();
        var client = new AnthropicClient
        {
            ApiKey = FakeKey,
            HttpClient = new HttpClient(handler),
            MaxRetries = 0,
        };
        var options = ClaudeParserTests.Options();
        var parser = new ClaudeParser(
            ParserFactory.CreateChatClient(client, options.Candidates["haiku"]),
            Microsoft.Extensions.Options.Options.Create(options),
            TimeProvider.System,
            new ClaudeParserTests.RecordingTrace(),
            NullLogger<ClaudeParser>.Instance);
        return (parser, handler);
    }

    private static HttpResponseMessage Message(string text) => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
    {
        id = "msg_test",
        type = "message",
        role = "assistant",
        model = "claude-haiku-5-5",
        content = new[] { new { type = "text", text } },
        stop_reason = "end_turn",
        stop_sequence = (string?)null,
        usage = new { input_tokens = 1200, output_tokens = 80 },
    }));

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task TheStructuredOutputReachesTheWireAsOutputConfigFormat()
    {
        var (parser, handler) = Start();
        handler.Answer = () => Message("{\"requests\":[" + Item + "]}");

        var parsed = await parser.ParseAsync("pay the water", new ParseContext("open_question which_bill", MaskMap.Empty), Token);

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
        Assert.Equal((1200, 80), (usage.InputTokens, usage.OutputTokens));
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
