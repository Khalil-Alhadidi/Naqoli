using System.Globalization;
using Agent.Core.Text;
using Agent.Web.Resources;
using Microsoft.Extensions.Localization;

namespace Agent.Web.Services;

/// <summary>
/// Figures inside the phone frame (D20): Arabic-Indic digits, the Arabic thousands separator (U+066C), and the currency
/// word from the resource file. The console outside the phone uses ASCII digits (<c>N0</c>, invariant).
/// </summary>
public static class PhoneFormat
{
    /// <summary>ARABIC THOUSANDS SEPARATOR.</summary>
    public const char ThousandsSeparator = (char)0x066C;

    /// <summary>A whole number with Arabic-Indic digits and the Arabic thousands separator, no currency.</summary>
    public static string Number(long value)
        => ArabicDigits.ToArabicIndic(value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ThousandsSeparator));

    /// <summary>An amount in dinars: <see cref="Number"/> and the currency word (<c>Currency_iqd</c>).</summary>
    public static string Amount(long amountIqd, IStringLocalizer<PhoneStrings> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Number(amountIqd) + " " + text["Currency_iqd"].Value;
    }

    /// <summary>Any text with its ASCII digits as Arabic-Indic digits (a reference, a code, a phone).</summary>
    public static string Digits(string value) => ArabicDigits.ToArabicIndic(value);

    /// <summary>A figure in the console: ASCII digits with a comma, invariant.</summary>
    public static string Console(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
