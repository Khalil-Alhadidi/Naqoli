using System.Globalization;
using System.Text;
using Agent.Core.Text;

namespace Agent.Eval.Checks;

/// <summary>The verdict of the outbound-text check: <see cref="Kind"/> names what leaked, never the text.</summary>
/// <param name="Ok">True when no text handed to the parser holds a number or figure it must not.</param>
/// <param name="Kind"><see cref="OutboundTextCheck.Phone"/>, <see cref="OutboundTextCheck.Account"/> or <see cref="OutboundTextCheck.Balance"/>; null when ok.</param>
public sealed record OutboundVerdict(bool Ok, string? Kind)
{
    public static OutboundVerdict Clean { get; } = new(true, null);
}

/// <summary>
/// D12's first per-case check (rule 5): the text handed to the parse call holds no phone number, no account number and
/// no balance. Each text is normalised first: digits to ASCII (<see cref="ArabicDigits.ToAscii"/>), then whitespace,
/// <c>-</c>, <c>+</c>, <c>,</c> and the Arabic thousands separator (U+066C) removed, so <c>0780 555 0101</c>,
/// <c>+964 780 555 0101</c> and Arabic-Indic digits are all caught.
/// </summary>
/// <remarks>
/// The checks are the ones the data can decide:
/// <list type="bullet">
/// <item><b>Phone:</b> any wallet number of the seed (11 digits, <c>07...</c>), or the same number with <c>964</c> in
/// place of the leading <c>0</c>.</item>
/// <item><b>Account:</b> any bill subscriber number of the seed (the account numbers a user could type). An account
/// number the wallet does not hold cannot be known, so it is not checked.</item>
/// <item><b>Balance:</b> the user's balance figure as digits (before the case, and the balance after a card when one
/// was built), when it has at least four digits; a substring match, so the check errs towards failing.</item>
/// </list>
/// Until milestone 6 masks numbers before the parse call, a case whose user types a phone number fails this check.
/// </remarks>
public static class OutboundTextCheck
{
    public const string Phone = "phone";
    public const string Account = "account";
    public const string Balance = "balance";

    private const int MinimumBalanceDigits = 4;

    /// <summary>Checks <paramref name="texts"/> for the wallet numbers <paramref name="accountNumbers"/> and the balance <paramref name="balanceBefore"/>.</summary>
    public static OutboundVerdict Check(IReadOnlyList<string> texts, IReadOnlyList<string> accountNumbers, long balanceBefore)
        => Check(texts, accountNumbers, [], [balanceBefore]);

    /// <summary>Checks <paramref name="texts"/> for wallet numbers, subscriber numbers and any of <paramref name="balances"/>.</summary>
    public static OutboundVerdict Check(
        IReadOnlyList<string> texts,
        IReadOnlyList<string> accountNumbers,
        IReadOnlyList<string> subscriberNumbers,
        IReadOnlyCollection<long> balances)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(accountNumbers);
        ArgumentNullException.ThrowIfNull(subscriberNumbers);
        ArgumentNullException.ThrowIfNull(balances);

        var figures = balances
            .Where(b => b >= 0)
            .Select(b => b.ToString(CultureInfo.InvariantCulture))
            .Where(b => b.Length >= MinimumBalanceDigits)
            .ToList();

        foreach (var text in texts)
        {
            var normalized = Normalize(text);
            foreach (var number in accountNumbers)
            {
                if (number.Length == 0)
                    continue;
                if (normalized.Contains(number, StringComparison.Ordinal)
                    || (number.StartsWith('0') && normalized.Contains("964" + number[1..], StringComparison.Ordinal)))
                {
                    return new OutboundVerdict(false, Phone);
                }
            }

            if (subscriberNumbers.Any(s => s.Length > 0 && normalized.Contains(s, StringComparison.Ordinal)))
                return new OutboundVerdict(false, Account);

            if (figures.Any(f => normalized.Contains(f, StringComparison.Ordinal)))
                return new OutboundVerdict(false, Balance);
        }

        return OutboundVerdict.Clean;
    }

    /// <summary>The text with ASCII digits and without spaces, dashes, plus signs and thousands separators.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ascii = ArabicDigits.ToAscii(text);
        var builder = new StringBuilder(ascii.Length);
        foreach (var c in ascii)
        {
            if (char.IsWhiteSpace(c) || c is '-' or '+' or ',' or '٬' || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
                continue;
            builder.Append(c);
        }

        return builder.ToString();
    }
}
