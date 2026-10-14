using Agent.Core.Requests;

namespace Agent.Core.Parsing;

/// <summary>
/// The parse call (rule 2): one call per message, read only, returning the strict structure that code then validates
/// and checks. <see cref="ScriptedParser"/> and <see cref="ClaudeParser"/> implement it.
/// </summary>
public interface IParser
{
    /// <summary>
    /// The parser's name, for the trace (<c>parse:&lt;name&gt;</c>) and the eval report: <c>scripted</c>, or
    /// <c>claude:&lt;candidate&gt;</c>.
    /// </summary>
    string Name { get; }

    /// <summary>Reads <paramref name="text"/> (already masked) into a request; anything it cannot read is <see cref="RequestIntent.Unclear"/>.</summary>
    Task<ParsedRequest> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken);
}
