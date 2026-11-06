using System.Globalization;
using System.Text.RegularExpressions;
using Agent.Core.Requests;

namespace Agent.Core.Text;

/// <summary>
/// Reads a typed answer to <c>which_amount</c> as an amount, in code, before any parse call (17 Oct 2026). Pure. The text
/// must be an amount and nothing else: digits in any digit form (a comma between groups of three, matching rule 19; a
/// dot is never a decimal point here, so such a text does not read), or Khalil's number words
/// (<see cref="AmountTextWords"/>), joined by "and" as its own word or written against the next word, with a scale word
/// (thousand, two thousand, thousands, million, two million, millions) and an optional trailing word for dinar.
/// </summary>
/// <remarks>
/// What it gives: a number followed by a scale word is (number, <see cref="AmountUnit.Thousand"/> or
/// <see cref="AmountUnit.Million"/>); a scale word alone is (1, scale), and the dual word is (2, scale); a bare number,
/// in digits or in words, is (number, <see cref="AmountUnit.None"/>), so that matching rule 9 decides it
/// (<c>AmountRule</c>); a bare number with the word for dinar is (number, <see cref="AmountUnit.Dinars"/>). A unit
/// written just before the word for one hundred multiplies it; a unit before a tens word adds to it. Two or more scale
/// groups add up in dinars (one million and five hundred thousand is (1,500,000, <see cref="AmountUnit.Dinars"/>)).
/// Fraction words (6 Nov 2026), read as the parse prompt reads them: "and" with the word for a quarter or a half after
/// one group adds it (three thousand and a quarter is (3.25, <see cref="AmountUnit.Thousand"/>)); the word for "less"
/// with a fraction takes it away (a million less a quarter is (0.75, <see cref="AmountUnit.Million"/>)); a fraction
/// just before the singular scale word is that fraction of it (a quarter of a million is
/// (0.25, <see cref="AmountUnit.Million"/>)); a fraction alone is (0.25 or 0.5, <see cref="AmountUnit.None"/>), which
/// the checks refuse below the minimum. A fraction with two or more scale groups, or followed by anything but the word
/// for dinar, does not read. Three forms whose meaning is Khalil's to rule do not read either (review of 6 Nov 2026), so
/// they go to the model as any message does: a fraction after a bare hundred or more, a bare number straight after
/// the millions, and a short counting form (three to ten) straight before the singular scale word.
/// Words are compared after <see cref="ArabicText.Normalize"/> on both sides.
/// </remarks>
public static partial class AmountText
{
    /// <summary>Reads <paramref name="text"/> as an amount; false when it is anything else.</summary>
    public static bool TryRead(string text, out decimal value, out AmountUnit unit)
    {
        ArgumentNullException.ThrowIfNull(text);
        value = 0;
        unit = AmountUnit.None;

        if (!TryTokenize(text, out var tokens) || tokens.Count == 0)
            return false;

        var reader = new Reader(tokens);

        // A fraction first: alone, or that fraction of the singular scale word after it.
        if (reader.Fraction() is { } leading)
        {
            var leadingScale = reader.SingularScale();
            var leadingDinars = reader.Take(AmountWordKind.Dinar);
            if (!reader.AtEnd)
                return false;
            value = leading;
            unit = Unit(leadingScale, leadingDinars);
            return true;
        }

        var groups = new List<(decimal Count, int Scale)>();
        decimal fraction = 0;
        while (true)
        {
            if (reader.Group() is not { } group)
                return false;
            groups.Add(group);

            // "less" and a fraction takes it away; "and" and a fraction adds it. Either ends the amount.
            if (reader.Take(AmountWordKind.Less))
            {
                if (reader.Fraction() is not { } less)
                    return false;
                fraction = -less;
                break;
            }

            if (!reader.Take(AmountWordKind.And))
                break;
            if (reader.Fraction() is { } more)
            {
                fraction = more;
                break;
            }
        }

        var dinars = reader.Take(AmountWordKind.Dinar);
        if (!reader.AtEnd)
            return false;

        // Scales come largest first, and only the last group may have none.
        for (var i = 1; i < groups.Count; i++)
        {
            if (groups[i].Scale >= groups[i - 1].Scale)
                return false;
        }

        if (groups.Count == 1)
        {
            var (count, scale) = groups[0];

            // A fraction after a bare hundred or more ("a hundred and a half") is not read: the half may be of the
            // hundred or of the thousand, and that reading is Khalil's to rule (review of 6 Nov 2026).
            if (fraction != 0 && scale == ScaleNone && count >= BareCountTooLargeForAFraction)
                return false;

            try
            {
                count += fraction;
            }
            catch (OverflowException)
            {
                return false;
            }

            if (count <= 0)
                return false;
            value = count;
            unit = Unit(scale, dinars);
            return true;
        }

        // A fraction is read with one group only; with more, the text goes to the model.
        if (fraction != 0)
            return false;

        // A bare number straight after the millions ("a million and five hundred") is not read: it may count thousands
        // or dinars, and that reading is Khalil's to rule (review of 6 Nov 2026). After the thousands it is dinars.
        for (var i = 1; i < groups.Count; i++)
        {
            if (groups[i].Scale == ScaleNone && groups[i - 1].Scale == ScaleMillion)
                return false;
        }

        decimal total;
        try
        {
            total = groups.Sum(g => g.Count * Multiplier(g.Scale));
        }
        catch (OverflowException)
        {
            return false;
        }

        if (total <= 0)
            return false;
        value = total;
        unit = AmountUnit.Dinars;
        return true;
    }

    /// <summary>A bare count from here up takes no fraction word (see <see cref="TryRead"/>).</summary>
    private const decimal BareCountTooLargeForAFraction = 100m;

    private const int ScaleNone = 0;
    private const int ScaleThousand = 1;
    private const int ScaleMillion = 2;

    private static AmountUnit Unit(int scale, bool dinars) => scale switch
    {
        ScaleMillion => AmountUnit.Million,
        ScaleThousand => AmountUnit.Thousand,
        _ => dinars ? AmountUnit.Dinars : AmountUnit.None,
    };

    private static decimal Multiplier(int scale) => scale switch
    {
        ScaleMillion => 1_000_000m,
        ScaleThousand => 1_000m,
        _ => 1m,
    };

    /// <summary>A word of the text, or a number written in digits (<see cref="Digits"/> is then set).</summary>
    private readonly record struct Token(AmountWordKind Kind, int Value, decimal? Digits = null, bool Construct = false);

    /// <summary>
    /// Splits the normalised text on spaces into words of <see cref="AmountTextWords"/> and digit numbers; "and"
    /// written against a known word becomes its own token. False for any other word.
    /// </summary>
    private static bool TryTokenize(string text, out List<Token> tokens)
    {
        tokens = [];
        var normalized = ArabicText.Normalize(text);
        if (normalized.Length == 0)
            return false;

        var words = AmountTextWords.Words;
        var and = AmountTextWords.And;
        foreach (var word in normalized.Split(' '))
        {
            if (DigitNumber().IsMatch(word))
            {
                if (!decimal.TryParse(word, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number))
                    return false;
                tokens.Add(new Token(AmountWordKind.Unit, 0, number));
            }
            else if (words.TryGetValue(word, out var known))
            {
                tokens.Add(new Token(known.Kind, known.Value, Construct: known.Construct));
            }
            else if (word.Length > and.Length
                && word.StartsWith(and, StringComparison.Ordinal)
                && words.TryGetValue(word[and.Length..], out var joined)
                && joined.Kind is not AmountWordKind.And and not AmountWordKind.Dinar and not AmountWordKind.Less)
            {
                tokens.Add(new Token(AmountWordKind.And, 0));
                tokens.Add(new Token(joined.Kind, joined.Value, Construct: joined.Construct));
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads the tokens left to right.</summary>
    private sealed class Reader(List<Token> tokens)
    {
        private int position;

        public bool AtEnd => position == tokens.Count;

        /// <summary>Takes the next token when it is of <paramref name="kind"/> (and is not a digit number).</summary>
        public bool Take(AmountWordKind kind)
        {
            if (Peek(0) is { Digits: null } token && token.Kind == kind)
            {
                position++;
                return true;
            }

            return false;
        }

        /// <summary>Takes the next token when it is a fraction word, and gives the fraction (0.25 or 0.5).</summary>
        public decimal? Fraction()
        {
            if (Peek(0) is { Digits: null, Kind: AmountWordKind.Fraction } token)
            {
                position++;
                return token.Value / 100m;
            }

            return null;
        }

        /// <summary>Takes the next token when it is the singular word for thousand or million; scale none otherwise.</summary>
        public int SingularScale()
        {
            if (Peek(0) is { Digits: null, Value: 1 } token && token.Kind is AmountWordKind.Thousand or AmountWordKind.Million)
            {
                position++;
                return token.Kind == AmountWordKind.Million ? ScaleMillion : ScaleThousand;
            }

            return ScaleNone;
        }

        /// <summary>A count with its scale word (thousand or million), or a bare count (scale none); null when neither.</summary>
        public (decimal Count, int Scale)? Group()
        {
            // A short counting form straight before the singular scale word is not read: the same words name a third,
            // a fifth and so on of the unit, and that reading is Khalil's to rule (review of 6 Nov 2026).
            if (Peek(0) is { Digits: null, Kind: AmountWordKind.Unit, Construct: true }
                && Peek(1) is { Digits: null, Value: 1, Kind: AmountWordKind.Thousand or AmountWordKind.Million })
            {
                return null;
            }

            var count = Count();
            if (Peek(0) is { Digits: null } scaleWord && scaleWord.Kind is AmountWordKind.Thousand or AmountWordKind.Million)
            {
                var scale = scaleWord.Kind == AmountWordKind.Million ? ScaleMillion : ScaleThousand;
                switch (scaleWord.Value)
                {
                    case 1:
                        position++;
                        return (count ?? 1, scale);
                    case 2 when count is null:
                        position++;
                        return (2, scale);
                    case 0 when count is not null:
                        position++;
                        return (count.Value, scale);
                    default:
                        return null;
                }
            }

            return count is { } bare ? (bare, ScaleNone) : null;
        }

        /// <summary>
        /// A number below one thousand in words (hundreds, then a teen, a unit, or a unit and a tens word, "and" between
        /// them optional), or a number in digits; null when the next token starts neither.
        /// </summary>
        private decimal? Count()
        {
            if (Peek(0) is { Digits: { } digits })
            {
                position++;
                return digits;
            }

            decimal total = 0;
            var any = false;

            // Hundreds: a hundreds word, or a unit (1 to 9) just before the word for one hundred.
            if (Peek(0) is { Digits: null, Kind: AmountWordKind.Hundreds } hundreds)
            {
                total += hundreds.Value;
                position++;
                any = true;
            }
            else if (Peek(0) is { Digits: null, Kind: AmountWordKind.Unit, Value: >= 1 and <= 9 } multiplier
                && Peek(1) is { Digits: null, Kind: AmountWordKind.Hundreds, Value: 100 })
            {
                total += multiplier.Value * 100;
                position += 2;
                any = true;
            }

            if (any)
                SkipAndBefore(AmountWordKind.Unit, AmountWordKind.Teen, AmountWordKind.Tens);

            switch (Peek(0))
            {
                case { Digits: null, Kind: AmountWordKind.Teen or AmountWordKind.Tens } word:
                    total += word.Value;
                    position++;
                    return total;

                case { Digits: null, Kind: AmountWordKind.Unit } unit:
                    total += unit.Value;
                    position++;
                    if (unit.Value <= 9)
                    {
                        SkipAndBefore(AmountWordKind.Tens);
                        if (Peek(0) is { Digits: null, Kind: AmountWordKind.Tens } tens)
                        {
                            total += tens.Value;
                            position++;
                        }
                    }

                    return total;

                default:
                    return any ? total : null;
            }
        }

        /// <summary>Takes an "and" only when the token after it is a word of one of <paramref name="kinds"/>.</summary>
        private void SkipAndBefore(params AmountWordKind[] kinds)
        {
            if (Peek(0) is { Digits: null, Kind: AmountWordKind.And }
                && Peek(1) is { Digits: null } next
                && Array.IndexOf(kinds, next.Kind) >= 0)
            {
                position++;
            }
        }

        private Token? Peek(int ahead)
            => position + ahead < tokens.Count ? tokens[position + ahead] : null;
    }

    /// <summary>Digits only, or groups of three separated by commas (matching rule 19).</summary>
    [GeneratedRegex(@"^(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DigitNumber();
}
