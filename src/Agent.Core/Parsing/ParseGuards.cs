using System.Globalization;
using Agent.Core.Requests;
using Agent.Core.Trace;

namespace Agent.Core.Parsing;

/// <summary>
/// What <see cref="ParseGuards.Apply"/> did: the guarded requests, and how many requests each guard rewrote (0 when it
/// changed nothing).
/// </summary>
/// <param name="Requests">The requests with both guards applied; the same list as given when no request changed.</param>
/// <param name="DinarUnit">The number of requests <see cref="DinarUnitGuard"/> rewrote.</param>
/// <param name="OtherBalance">The number of requests <see cref="OtherBalanceGuard"/> rewrote.</param>
public sealed record ParseGuardResult(IReadOnlyList<ParsedRequest> Requests, int DinarUnit, int OtherBalance);

/// <summary>
/// The code guards both parsers apply to the validated requests of one message, in order: the dinar-unit guard
/// (<see cref="DinarUnitGuard"/>), then the other-balance guard (<see cref="OtherBalanceGuard"/>). Each only ever makes a
/// request smaller or a refusal, never a card the parse did not ask for. <see cref="Apply"/> is pure; the parsers write
/// what fired to the trace with <see cref="Record"/> (security review M2 of the prompt version 7 round).
/// </summary>
public static class ParseGuards
{
    /// <summary>The trace step (<see cref="TraceZone.Code"/>) when the dinar-unit guard rewrote a request; the detail is the count.</summary>
    public const string DinarUnitStep = "guard:dinar_unit";

    /// <summary>The trace step (<see cref="TraceZone.Code"/>) when the other-balance guard rewrote a request; the detail is the count.</summary>
    public const string OtherBalanceStep = "guard:other_balance";

    /// <summary>Both guards applied, with the number of requests each rewrote.</summary>
    /// <param name="requests">The validated requests of one message.</param>
    /// <param name="maskedText">The message as the parser was given it (numbers masked; the words are the user's own).</param>
    /// <param name="dinarWords"><see cref="AgentOptions.DinarWords"/>; empty turns the dinar-unit guard off.</param>
    public static ParseGuardResult Apply(
        IReadOnlyList<ParsedRequest> requests, string maskedText, IReadOnlyCollection<string> dinarWords)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var dinars = DinarUnitGuard.Apply(requests, maskedText, dinarWords);
        var guarded = OtherBalanceGuard.Apply(dinars);
        return new ParseGuardResult(guarded, Rewritten(requests, dinars), Rewritten(dinars, guarded));
    }

    /// <summary>
    /// Writes one <see cref="TraceZone.Code"/> step per guard that rewrote a request, with the count as its detail: never
    /// an amount, a name or any text of the model or the user.
    /// </summary>
    public static void Record(ParseGuardResult result, ITraceSink? trace)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (trace is null)
            return;
        if (result.DinarUnit > 0)
            trace.Record(TraceZone.Code, DinarUnitStep, result.DinarUnit.ToString(CultureInfo.InvariantCulture));
        if (result.OtherBalance > 0)
            trace.Record(TraceZone.Code, OtherBalanceStep, result.OtherBalance.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The number of positions where a guard returned a new request (each guard keeps an unchanged request as it is).</summary>
    private static int Rewritten(IReadOnlyList<ParsedRequest> before, IReadOnlyList<ParsedRequest> after)
        => ReferenceEquals(before, after) ? 0 : before.Where((request, i) => !ReferenceEquals(request, after[i])).Count();
}
