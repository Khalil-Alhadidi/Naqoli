namespace Agent.Core.Text;

/// <summary>
/// Writes a whole amount of dinars in words, from Khalil's list and examples (17 Oct 2026). The card shows the result
/// under the figure as a second guard; it is computed at render and never stored.
/// </summary>
/// <remarks>
/// <para>Range: 1,000 to 99,999,999 dinars; outside it there are no words (null), and nothing throws for an amount.</para>
/// <para>A group below 1,000: the hundreds word, then the rest; 1 to 19 a unit word; 20 to 99 the unit, then the tens
/// (five and twenty), or the tens alone; hundreds and the rest joined with the joiner.</para>
/// <para>Thousands: 1,000 the one form; 2,000 the dual; 3,000 to 10,000 the construct and the plural; from 11,000 the
/// group's words and the one form. Millions likewise. The groups (millions, thousands, the rest) are joined with the
/// joiner. One space between words; the joiner is attached to the word after it.</para>
/// </remarks>
public static class AmountWords
{
    /// <summary>The smallest amount written in words.</summary>
    public const long Minimum = 1_000;

    /// <summary>The largest amount written in words.</summary>
    public const long Maximum = 99_999_999;

    /// <summary>The words for <paramref name="dinars"/>, or null outside <see cref="Minimum"/> to <see cref="Maximum"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="parts"/> has a list of the wrong length.</exception>
    public static string? Compose(long dinars, AmountWordParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        Check(parts.Units, AmountWordParts.UnitCount, nameof(parts.Units));
        Check(parts.Tens, AmountWordParts.TensCount, nameof(parts.Tens));
        Check(parts.Hundreds, AmountWordParts.HundredsCount, nameof(parts.Hundreds));
        Check(parts.Constructs, AmountWordParts.ConstructCount, nameof(parts.Constructs));

        if (dinars is < Minimum or > Maximum)
            return null;

        var millions = (int)(dinars / 1_000_000);
        var thousands = (int)(dinars / 1_000 % 1_000);
        var rest = (int)(dinars % 1_000);

        var groups = new List<string>(3);
        if (millions > 0)
            groups.Add(Scaled(millions, parts.Million, parts.TwoMillion, parts.Millions, parts));
        if (thousands > 0)
            groups.Add(Scaled(thousands, parts.Thousand, parts.TwoThousand, parts.Thousands, parts));
        if (rest > 0)
            groups.Add(Group(rest, parts));

        return Joined(groups, parts);
    }

    /// <summary>A count of thousands or millions (1 to 999) with its word.</summary>
    private static string Scaled(int count, string one, string two, string plural, AmountWordParts parts) => count switch
    {
        1 => one,
        2 => two,
        <= 10 => parts.Constructs[count - 3] + " " + plural,
        _ => Group(count, parts) + " " + one,
    };

    /// <summary>A group from 1 to 999.</summary>
    private static string Group(int value, AmountWordParts parts)
    {
        var words = new List<string>(2);
        var hundreds = value / 100;
        var below = value % 100;
        if (hundreds > 0)
            words.Add(parts.Hundreds[hundreds - 1]);
        if (below > 0)
            words.Add(BelowHundred(below, parts));
        return Joined(words, parts);
    }

    /// <summary>1 to 99: a unit word, the tens alone, or the unit and then the tens.</summary>
    private static string BelowHundred(int value, AmountWordParts parts)
    {
        if (value < 20)
            return parts.Units[value - 1];
        var tens = parts.Tens[(value / 10) - 2];
        var unit = value % 10;
        return unit == 0 ? tens : parts.Units[unit - 1] + " " + parts.And + tens;
    }

    private static string Joined(List<string> words, AmountWordParts parts) => string.Join(" " + parts.And, words);

    private static void Check(IReadOnlyList<string> list, int count, string name)
    {
        if (list is null || list.Count != count)
            throw new ArgumentException($"{name} holds {count} words.", nameof(AmountWordParts));
    }
}
