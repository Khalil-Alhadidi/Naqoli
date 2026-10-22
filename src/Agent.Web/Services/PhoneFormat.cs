using System.Globalization;
using Agent.Core;
using Agent.Core.Pipeline;
using Agent.Core.Text;
using Agent.Web.Resources;
using Microsoft.Extensions.Localization;

namespace Agent.Web.Services;

/// <summary>
/// Figures inside the phone frame (D20): Arabic-Indic digits, a comma between the thousands (Khalil, 22 Oct 2026: the
/// comma that hangs down, not the raised ARABIC THOUSANDS SEPARATOR U+066C the phone showed before), and the currency
/// word from the resource file. The console outside the phone uses ASCII digits (<c>N0</c>, invariant).
/// </summary>
public static class PhoneFormat
{
    /// <summary>The thousands separator inside the phone: the comma (U+002C), which hangs down.</summary>
    public const char ThousandsSeparator = ',';

    /// <summary>A whole number with Arabic-Indic digits and a comma between the thousands, no currency.</summary>
    public static string Number(long value)
        => ArabicDigits.ToArabicIndic(value.ToString("N0", CultureInfo.InvariantCulture));

    /// <summary>An amount in dinars: <see cref="Number"/> and the currency word (<c>Currency_iqd</c>).</summary>
    public static string Amount(long amountIqd, IStringLocalizer<PhoneStrings> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Number(amountIqd) + " " + text["Currency_iqd"].Value;
    }

    /// <summary>Any text with its ASCII digits as Arabic-Indic digits (a reference, a code, a phone).</summary>
    public static string Digits(string value) => ArabicDigits.ToArabicIndic(value);

    /// <summary>The Iraq date of <paramref name="instant"/> (<see cref="AgentClock.IraqOffset"/>), <c>dd/MM/yyyy</c> (security review R2 of milestone 9b), with Arabic-Indic digits.</summary>
    public static string Date(DateTimeOffset instant)
        => ArabicDigits.ToArabicIndic(instant.ToOffset(AgentClock.IraqOffset).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));

    /// <summary>The Iraq time of <paramref name="instant"/>, <c>HH:mm</c>, with Arabic-Indic digits: the phone's status bar (UI pass task 7).</summary>
    public static string Time(DateTimeOffset instant)
        => ArabicDigits.ToArabicIndic(instant.ToOffset(AgentClock.IraqOffset).ToString("HH:mm", CultureInfo.InvariantCulture));

    /// <summary>
    /// The label of a question option: the store option's string; else the option's name (<see cref="OptionName"/>), then
    /// the product type's word (<c>Product_type_credit</c>, <c>Product_type_internet</c>) when the option is a product
    /// (ruling 3 of milestone 9b), then the amount when it has one; else the amount, or the value.
    /// </summary>
    public static string OptionLabel(Option option, IStringLocalizer<PhoneStrings> text)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(text);

        if (option.Value == Option.StoreInternet)
            return text["Option_store_internet"].Value;
        if (OptionName(option) is { } display)
        {
            var label = display;
            if (ProductTypeWord(option.ProductType, text) is { } word)
                label += " " + word;
            return option.AmountIqd is { } price ? label + " " + Amount(price, text) : label;
        }

        return option.AmountIqd is { } amount ? Amount(amount, text) : option.Value;
    }

    /// <summary>
    /// The name an option shows: its display name, and for the bill option of a <c>person_or_bill</c> question
    /// (<see cref="OptionSide.Bill"/>, Khalil, 20 Oct 2026) the biller's name before it, as the wallet holds both, since
    /// the label is the same name as the person's. Null when the option has no display name.
    /// </summary>
    public static string? OptionName(Option option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return option is { Side: OptionSide.Bill, BillerName: { } biller, Display: { } label } && !string.Equals(biller, label, StringComparison.Ordinal)
            ? biller + " " + label
            : option.Display;
    }

    /// <summary>
    /// The type word of a product type (<c>Product_type_credit</c>, <c>Product_type_internet</c>), or null when there is
    /// no type or no resource for it: the key is built from a wallet value (security review M3 of milestone 9b).
    /// </summary>
    public static string? ProductTypeWord(string? type, IStringLocalizer<PhoneStrings> text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return type is not null && text["Product_type_" + type] is { ResourceNotFound: false } word ? word.Value : null;
    }

    /// <summary>A figure in the console: ASCII digits with a comma, invariant.</summary>
    public static string Console(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
