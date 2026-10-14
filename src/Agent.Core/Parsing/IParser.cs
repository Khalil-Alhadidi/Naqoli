using Agent.Core.Requests;

namespace Agent.Core.Parsing;

/// <summary>
/// The parse call (rule 2): one call per message, read only, returning the strict structure that code then validates
/// and checks. <see cref="ScriptedParser"/> is the only implementation until milestone 6 brings the model.
/// </summary>
public interface IParser
{
    /// <summary>Reads <paramref name="text"/> (already masked) into a request; anything it cannot read is <see cref="RequestIntent.Unclear"/>.</summary>
    Task<ParsedRequest> ParseAsync(string text, ParseContext context, CancellationToken cancellationToken);
}
