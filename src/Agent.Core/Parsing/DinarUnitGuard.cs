using Agent.Core.Requests;
using Agent.Core.Text;

namespace Agent.Core.Parsing;

/// <summary>
/// The dinar-unit guard (Khalil, 4 Nov 2026, the Flash round): when a parsed request has an amount with
/// <see cref="AmountUnit.None"/> and the user's message holds one of <see cref="AgentOptions.DinarWords"/>, the unit
/// becomes <see cref="AmountUnit.Dinars"/>. A model that drops the written unit (DeepSeek V4.1 Flash on ADD-13: "500
/// dinars" returned as a bare 500) would otherwise have its bare number read as thousands by matching rule 9
/// (<see cref="Requests.AmountRule"/>, untouched). A request with <see cref="AmountUnit.Dinars"/>,
/// <see cref="AmountUnit.Thousand"/> or <see cref="AmountUnit.Million"/>, a request with no amount and a message with
/// no dinar word pass through unchanged. A word matches when its <see cref="ArabicText.Key"/> equals a configured word's
/// key, the comparison the contact resolver uses. It runs after <see cref="ParsedRequestValidator"/> in both parsers.
/// </summary>
public static class DinarUnitGuard
{
    /// <summary>The requests with the guard applied; the same list when no request changes.</summary>
    /// <param name="requests">The validated requests of one message.</param>
    /// <param name="maskedText">The message as the parser was given it (numbers masked; the words are the user's own).</param>
    /// <param name="dinarWords"><see cref="AgentOptions.DinarWords"/>; empty turns the guard off.</param>
    public static IReadOnlyList<ParsedRequest> Apply(
        IReadOnlyList<ParsedRequest> requests, string maskedText, IReadOnlyCollection<string> dinarWords)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(maskedText);
        ArgumentNullException.ThrowIfNull(dinarWords);

        if (dinarWords.Count == 0 || !requests.Any(NeedsUnit) || !HasDinarWord(maskedText, dinarWords))
            return requests;

        return [.. requests.Select(r => NeedsUnit(r) ? r with { AmountUnit = AmountUnit.Dinars } : r)];
    }

    /// <summary>True when one word of <paramref name="text"/> has the key of one of <paramref name="dinarWords"/>.</summary>
    public static bool HasDinarWord(string text, IReadOnlyCollection<string> dinarWords)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dinarWords);

        var keys = dinarWords.Where(w => !string.IsNullOrWhiteSpace(w)).Select(ArabicText.Key).ToHashSet(StringComparer.Ordinal);
        return keys.Count > 0 && ArabicText.Words(text).Any(w => keys.Contains(ArabicText.Key(w)));
    }

    private static bool NeedsUnit(ParsedRequest request) => request.AmountValue is not null && request.AmountUnit == AmountUnit.None;
}
