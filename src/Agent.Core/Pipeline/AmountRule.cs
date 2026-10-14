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

    /// <summary>A bare number below 1,000 (rule 9): ask which reading, with these amounts in dinars as options.</summary>
    public sealed record Ask(IReadOnlyList<long> Options) : AmountResolution;

    /// <summary>The amount in whole dinars. It still goes through the checks (minimum, caps, balance).</summary>
    public sealed record Dinars(long AmountIqd) : AmountResolution;

    /// <summary>
    /// No usable amount; <see cref="Reason"/> is <see cref="ReasonCodes.NotUnderstood"/>, or
    /// <see cref="ReasonCodes.AmountBelowMinimum"/> when a bare number's only whole readings are below the minimum.
    /// </summary>
    public sealed record Invalid(string Reason) : AmountResolution;
}

/// <summary>
/// Matching rule 9 (a bare number below 1,000 is asked about; 1,000 or more is dinars as written) and rule 10 (a missing
/// amount is asked for). A reading below <see cref="AgentOptions.MinAmountIqd"/> is not offered, and a bare number with
/// no reading left is <see cref="ReasonCodes.AmountBelowMinimum"/> (Khalil, 14 Oct 2026). Pure. A currency other than dinars (<see cref="ParsedRequest.CurrencyText"/>) is not handled
/// here: the orchestrator refuses it with <see cref="ReasonCodes.DinarsOnly"/> first.
/// </summary>
public static class AmountRule
{
    /// <summary>A bare number below this is asked about (rule 9).</summary>
    public const long BareNumberThreshold = 1_000;

    // Rule 9: the thousand and the million reading of a bare number below the threshold.
    private static readonly long[] ReadingMultipliers = [1_000, 1_000_000];

    public static AmountResolution Resolve(ParsedRequest request, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (request.AmountValue is not { } value)
            return new AmountResolution.Missing();

        if (value <= 0)
            return NotUnderstood();

        switch (request.AmountUnit)
        {
            case AmountUnit.None when value < BareNumberThreshold:
                {
                    // Rule 9: the thousand and the million reading, each only as whole dinars from the minimum up to the
                    // per-transaction cap. A reading below the minimum is dropped (Khalil, 14 Oct 2026).
                    var readings = new List<long>(2);
                    var belowMinimum = false;
                    foreach (var multiplier in ReadingMultipliers)
                    {
                        if (!TryWholeDinars(value, multiplier, out var amount) || amount > options.PerTransactionCapIqd)
                            continue;
                        if (amount < options.MinAmountIqd)
                            belowMinimum = true;
                        else
                            readings.Add(amount);
                    }

                    if (readings.Count > 0)
                        return new AmountResolution.Ask(readings);
                    return belowMinimum ? new AmountResolution.Invalid(ReasonCodes.AmountBelowMinimum) : NotUnderstood();
                }

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
