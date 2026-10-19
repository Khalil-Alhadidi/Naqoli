using System.Globalization;
using Agent.Core;
using Agent.Core.Pipeline;
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

    /// <summary>The Iraq date of <paramref name="instant"/> (<see cref="AgentClock.IraqOffset"/>), <c>yyyy/MM/dd</c>, with Arabic-Indic digits.</summary>
    public static string Date(DateTimeOffset instant)
        => ArabicDigits.ToArabicIndic(instant.ToOffset(AgentClock.IraqOffset).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));

    /// <summary>
    /// The label of a question option: the store option's string; else the display name, then the product type's word
    /// (<c>Product_type_credit</c>, <c>Product_type_internet</c>) when the option is a product (ruling 3 of milestone 9b),
    /// then the amount when it has one; else the amount, or the value.
    /// </summary>
    public static string OptionLabel(Option option, IStringLocalizer<PhoneStrings> text)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(text);

        if (option.Value == Option.StoreInternet)
            return text["Option_store_internet"].Value;
        if (option.Display is { } display)
        {
            // Security review M3: the key is built from a wallet value; a type without a resource gets no word.
            var label = display;
            if (option.ProductType is { } type && text["Product_type_" + type] is { ResourceNotFound: false } word)
                label += " " + word.Value;
            return option.AmountIqd is { } price ? label + " " + Amount(price, text) : label;
        }

        return option.AmountIqd is { } amount ? Amount(amount, text) : option.Value;
    }

    /// <summary>A figure in the console: ASCII digits with a comma, invariant.</summary>
    public static string Console(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
