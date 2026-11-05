using Agent.Core.Requests;

namespace Agent.Core.Parsing;

/// <summary>
/// The reason guard (the milestone 11 preparation, 5 Nov 2026): a request whose <see cref="ParsedRequest.UnsupportedReason"/>
/// is set but whose <see cref="ParsedRequest.Intent"/> is not <see cref="RequestIntent.Unsupported"/> becomes
/// <see cref="RequestIntent.Unsupported"/>, the reason and every slot kept. The validator keeps the reason whatever the
/// intent, and the orchestrator reads it only on unsupported requests, so a reason the model gave with another intent
/// (GAP-04 read as a transfer with <c>choose_account</c>) would otherwise build a plain card the model itself flagged.
/// With the guard the reason is always honoured: the refusal, or for <c>choose_account</c> with a recipient and an amount
/// the card with its note (the orchestrator's existing branch). Every other request passes through unchanged. It runs
/// after <see cref="DinarUnitGuard"/> and <see cref="OtherBalanceGuard"/> in both parsers (<see cref="ParseGuards"/>).
/// </summary>
public static class ReasonIntentGuard
{
    /// <summary>The requests with the guard applied; the same list when no request changes.</summary>
    /// <param name="requests">The validated requests of one message.</param>
    public static IReadOnlyList<ParsedRequest> Apply(IReadOnlyList<ParsedRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (!requests.Any(CarriesAReasonWithAnotherIntent))
            return requests;

        return [.. requests.Select(r => CarriesAReasonWithAnotherIntent(r) ? r with { Intent = RequestIntent.Unsupported } : r)];
    }

    private static bool CarriesAReasonWithAnotherIntent(ParsedRequest request)
        => request.Intent != RequestIntent.Unsupported && request.UnsupportedReason is not null;
}
