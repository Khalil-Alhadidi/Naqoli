using Agent.Core.Requests;

namespace Agent.Core.Parsing;

/// <summary>
/// The code guards both parsers apply to the validated requests of one message, in order: the dinar-unit guard
/// (<see cref="DinarUnitGuard"/>), then the other-balance guard (<see cref="OtherBalanceGuard"/>). Each only ever makes a
/// request smaller or a refusal, never a card the parse did not ask for.
/// </summary>
public static class ParseGuards
{
    /// <summary>The requests with both guards applied; the same list when no request changes.</summary>
    /// <param name="requests">The validated requests of one message.</param>
    /// <param name="maskedText">The message as the parser was given it (numbers masked; the words are the user's own).</param>
    /// <param name="dinarWords"><see cref="AgentOptions.DinarWords"/>; empty turns the dinar-unit guard off.</param>
    public static IReadOnlyList<ParsedRequest> Apply(
        IReadOnlyList<ParsedRequest> requests, string maskedText, IReadOnlyCollection<string> dinarWords)
        => OtherBalanceGuard.Apply(DinarUnitGuard.Apply(requests, maskedText, dinarWords));
}
