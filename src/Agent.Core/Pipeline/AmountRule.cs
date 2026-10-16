using Agent.Core.Requests;

namespace Agent.Core.Pipeline;

/// <summary>The amount of a request, read by matching rules 9 and 10.</summary>
public abstract record AmountResolution
{
    private AmountResolution()
    {
    }

    /// <summary>No amount was given (rule 10): ask, and the user types the answer (<c>which_amount</c>, typed).</summary>
    public sealed record Missing : AmountResolution;

    /// <summary>The amount in whole dinars. It still goes through the checks (minimum, caps, balance).</summary>
    public sealed record Dinars(long AmountIqd) : AmountResolution;

    /// <summary>No usable amount; <see cref="Reason"/> is <see cref="ReasonCodes.NotUnderstood"/>.</summary>
    public sealed record Invalid(string Reason) : AmountResolution;
}

/// <summary>
/// Matching rule 9 as of 16 Oct 2026 (Khalil): a bare number with no unit below 1,000 is read in thousands (2.5 is 2,500;
/// 50 is 50,000); a bare number of 1,000 or more is dinars as written; million only when the word for million is written.
/// A bare number is never asked about. Rule 10: a missing amount is asked for. A reading that is not whole dinars is
/// <see cref="ReasonCodes.NotUnderstood"/>. The minimum and the caps are not the reader's concern: a reading below
/// <see cref="AgentOptions.MinAmountIqd"/> goes on to the checks, which refuse it
/// <see cref="ReasonCodes.AmountBelowMinimum"/>. Pure. A currency other than dinars
/// (<see cref="ParsedRequest.CurrencyText"/>) is not handled here: the orchestrator refuses it with
/// <see cref="ReasonCodes.DinarsOnly"/> first.
/// </summary>
public static class AmountRule
{
    /// <summary>A bare number below this is read in thousands (rule 9).</summary>
    public const long BareNumberThreshold = 1_000;

    public static AmountResolution Resolve(ParsedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AmountValue is not { } value)
            return new AmountResolution.Missing();

        if (value <= 0)
            return NotUnderstood();

        switch (request.AmountUnit)
        {
            case AmountUnit.None when value < BareNumberThreshold:
                // Rule 9 (Khalil, 16 Oct 2026): a bare number below 1,000 is thousands.
                return Whole(value, 1_000);

            case AmountUnit.None:
            case AmountUnit.Dinars:
                return Whole(value, 1);
            case AmountUnit.Thousand:
                return Whole(value, 1_000);

            case AmountUnit.Million:
                return Whole(value, 1_000_000);

            default:
                return NotUnderstood();
        }
    }

    private static AmountResolution Whole(decimal value, long multiplier)
        => TryWholeDinars(value, multiplier, out var amount) ? new AmountResolution.Dinars(amount) : NotUnderstood();

    private static AmountResolution.Invalid NotUnderstood() => new(ReasonCodes.NotUnderstood);

    /// <summary>True when <paramref name="value"/> times <paramref name="multiplier"/> is a whole number of dinars that fits a long.</summary>
    private static bool TryWholeDinars(decimal value, long multiplier, out long amount)
    {
        amount = 0;
        decimal product;
        try
        {
            product = value * multiplier;
        }
        catch (OverflowException)
        {
            return false;
        }

        if (product != decimal.Truncate(product) || product > long.MaxValue)
            return false;

        amount = (long)product;
        return true;
    }
}
