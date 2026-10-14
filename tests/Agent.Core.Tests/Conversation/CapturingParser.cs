using System.Collections.Concurrent;
using Agent.Core.Parsing;
using Agent.Core.Requests;

namespace Agent.Core.Tests.Conversation;

/// <summary>
/// A parser for tests: it records every text and context handed to the parse call, and answers a text it was given
/// with its request (compared as received, so a test names the masked text it expects); anything else is
/// <see cref="RequestIntent.Unclear"/>.
/// </summary>
internal sealed class CapturingParser(IReadOnlyDictionary<string, ParsedRequest> replies) : IParser
{
    public const string ParserName = "capturing";

    private readonly ConcurrentQueue<(string Text, ParseContext Context)> calls = new();

    public string Name => ParserName;

    public IReadOnlyList<(string Text, ParseContext Context)> Calls => [.. calls];

    public Task<ParsedRequest> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
    {
        calls.Enqueue((text, context));
        return Task.FromResult(replies.TryGetValue(text, out var request) ? request : new ParsedRequest { Intent = RequestIntent.Unclear });
    }
}
