using System.Collections.Concurrent;
using Agent.Core.Parsing;
using Agent.Core.Requests;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// A parser for tests: it records every text and context handed to the parse call, and answers a text it was given
/// with its message (compared as received, so a test names the masked text it expects); anything else is one
/// <see cref="RequestIntent.Unclear"/> request.
/// </summary>
internal sealed class CapturingParser : IParser
{
    public const string ParserName = "capturing";

    private readonly IReadOnlyDictionary<string, ParsedMessage> replies;
    private readonly ConcurrentQueue<(string Text, ParseContext Context)> calls = new();

    /// <summary>Each text answered with its message (every request of it, in order: D7).</summary>
    public CapturingParser(IReadOnlyDictionary<string, ParsedMessage> replies) => this.replies = replies;

    /// <summary>Each text answered with a message of its one request.</summary>
    public CapturingParser(IReadOnlyDictionary<string, ParsedRequest> replies)
        : this(replies.ToDictionary(r => r.Key, r => ParsedMessage.One(r.Value), StringComparer.Ordinal))
    {
    }

    public string Name => ParserName;

    public IReadOnlyList<(string Text, ParseContext Context)> Calls => [.. calls];

    public Task<ParsedMessage> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
    {
        calls.Enqueue((text, context));
        return Task.FromResult(replies.TryGetValue(text, out var message) ? message : ParsedMessage.One(new ParsedRequest { Intent = RequestIntent.Unclear }));
    }
}
