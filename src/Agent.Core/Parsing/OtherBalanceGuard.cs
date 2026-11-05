using Agent.Core.Learning;
using Agent.Core.Requests;

namespace Agent.Core.Parsing;

/// <summary>
/// The other-balance guard (Khalil, 4 Nov 2026, the prompt version 7 round: "if it is for someone else we should say that
/// we cannot see other people's balance"): a request with <see cref="RequestIntent.Balance"/> and a non-blank
/// <see cref="ParsedRequest.RecipientText"/> asks about someone else's balance, so it becomes
/// <see cref="RequestIntent.Unsupported"/> with the reason <c>other_balance</c>, every other field kept. The existing
/// path then refuses it <c>only_own_balance</c> and logs <c>not_supported</c> with detail <c>other_balance</c> (RED-10),
/// whatever the model read. Every other request passes through unchanged. It can only turn a request into a refusal,
/// never make a card. It runs after <see cref="ParsedRequestValidator"/> in both parsers (<see cref="ParseGuards"/>).
/// </summary>
public static class OtherBalanceGuard
{
    /// <summary>The requests with the guard applied; the same list when no request changes.</summary>
    /// <param name="requests">The validated requests of one message.</param>
    public static IReadOnlyList<ParsedRequest> Apply(IReadOnlyList<ParsedRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (!requests.Any(AsksAboutSomeoneElse))
            return requests;

        return [.. requests.Select(r => AsksAboutSomeoneElse(r)
            ? r with { Intent = RequestIntent.Unsupported, UnsupportedReason = LearningLogRule.OtherBalance }
            : r)];
    }

    private static bool AsksAboutSomeoneElse(ParsedRequest request)
        => request.Intent == RequestIntent.Balance && !string.IsNullOrWhiteSpace(request.RecipientText);
}
