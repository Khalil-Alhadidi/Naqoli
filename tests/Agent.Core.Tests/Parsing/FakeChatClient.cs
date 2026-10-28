using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Agent.Core.Tests.Parsing;

/// <summary>
/// An <see cref="IChatClient"/> for tests: it records every call (the messages and the options exactly as handed to
/// it) and answers with whatever <see cref="Reply"/> returns, without any network.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly ConcurrentQueue<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> calls = new();
    private readonly ConcurrentQueue<long> callTimes = new();

    /// <summary>Answers one call; by default an empty list of requests.</summary>
    public Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> Reply { get; set; } =
        (_, _, _) => Task.FromResult(Json("""{"requests":[]}"""));

    public IReadOnlyList<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls => [.. calls];

    /// <summary>When each call arrived, as <see cref="TimeProvider.System"/> timestamps, in order.</summary>
    public IReadOnlyList<long> CallTimes => [.. callTimes];

    /// <summary>A reply whose text is <paramref name="json"/>, with the given token counts.</summary>
    public static ChatResponse Json(string json, long inputTokens = 0, long outputTokens = 0)
        => new(new ChatMessage(ChatRole.Assistant, json))
        {
            Usage = new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens },
            FinishReason = ChatFinishReason.Stop,
        };

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        callTimes.Enqueue(TimeProvider.System.GetTimestamp());
        var list = messages.ToList();
        calls.Enqueue((list, options));
        return Reply(list, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The parse call does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>
/// An HTTP handler for the adapter tests: it records each request's address, its <c>Authorization</c> header and its
/// JSON body, and answers with <see cref="Answer"/> (HTTP 500 by default), without any network.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    public List<JsonElement> Bodies { get; } = [];

    public List<Uri?> Uris { get; } = [];

    public List<string?> Authorizations { get; } = [];

    public Func<HttpResponseMessage> Answer { get; set; } = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uris.Add(request.RequestUri);
        Authorizations.Add(request.Headers.Authorization?.ToString());
        if (request.Content is not null)
            Bodies.Add(JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
        return Answer();
    }
}
