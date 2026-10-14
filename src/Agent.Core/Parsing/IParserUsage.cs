namespace Agent.Core.Parsing;

/// <summary>
/// Implemented by a parser that can say what its last parse call cost (D12: the runner records time and cost per case).
/// The scripted parser reports zero tokens and zero cost.
/// </summary>
public interface IParserUsage
{
    /// <summary>The usage of the last completed parse call, or null before the first.</summary>
    ParseUsage? LastUsage { get; }
}
