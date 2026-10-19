using Agent.Core.Requests;

namespace Agent.Core.Parsing;

/// <summary>
/// The parse call (rule 2): one call per message, read only, returning the strict structure that code then validates
/// and checks: every request of the message (<see cref="ParsedMessage"/>). <see cref="ScriptedParser"/> and
/// <see cref="ClaudeParser"/> implement it.
/// </summary>
public interface IParser
{
    /// <summary>
    /// The parser's name, for the trace (<c>parse:&lt;name&gt;</c>) and the eval report: <c>scripted</c>, or
    /// <c>claude:&lt;candidate&gt;</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Reads <paramref name="text"/> (already masked) into every request it holds, in the order spoken (D7); anything it
    /// cannot read is one <see cref="RequestIntent.Unclear"/> request.
    /// </summary>
    Task<ParsedMessage> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken);
}
