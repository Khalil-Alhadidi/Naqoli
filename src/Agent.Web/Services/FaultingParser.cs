using Agent.Core.Parsing;
using Agent.Core.Requests;

namespace Agent.Web.Services;

/// <summary>
/// The console's model fault (milestone 8): an <see cref="IParser"/> decorator over the parser <c>Agent:Parser</c>
/// names. When <see cref="ModelFaultSwitch.Take"/> gives a fault, the call never reaches the parser underneath:
/// <see cref="ModelFaultMode.Unreachable"/> throws <see cref="ParserUnavailableException"/> (the phone shows
/// <c>model_unavailable</c>), and <see cref="ModelFaultMode.InvalidReply"/> returns what the structure validator returns
/// for a refused reply, a message of one unclear request with <see cref="ParsedRequest.Problem"/> <see cref="InjectedProblem"/> (the
/// conversation's <c>model_invalid_reply</c> path), as the runner's <c>RecordingParser</c> does. Otherwise it delegates.
/// </summary>
public sealed class FaultingParser(IParser inner, ModelFaultSwitch faults) : IParser, IParserUsage
{
    /// <summary>The problem of an injected invalid reply (the runner's value).</summary>
    public const string InjectedProblem = "injected";

    /// <summary>The parser underneath.</summary>
    public IParser Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>The parser underneath's name: the decorator is transparent in the trace and the reports.</summary>
    public string Name => Inner.Name;

    /// <summary>The parser underneath's usage, when it reports one.</summary>
    public ParseUsage? LastUsage => (Inner as IParserUsage)?.LastUsage;

    public Task<ParsedMessage> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);

        return faults.Take() switch
        {
            ModelFaultMode.Unreachable => throw new ParserUnavailableException("Injected fault: the model cannot be reached."),
            ModelFaultMode.InvalidReply => Task.FromResult(ParsedMessage.One(new ParsedRequest { Intent = RequestIntent.Unclear, Problem = InjectedProblem })),
            _ => Inner.ParseAsync(text, context, cancellationToken),
        };
    }
}
