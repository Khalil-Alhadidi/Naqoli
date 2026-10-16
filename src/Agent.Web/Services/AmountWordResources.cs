using System.Globalization;
using Agent.Core.Text;
using Agent.Web.Resources;
using Microsoft.Extensions.Localization;

namespace Agent.Web.Services;

/// <summary>
/// Builds the card's number words (<see cref="AmountWordParts"/>) from the phone's resource file: <c>Words_1</c> to
/// <c>Words_19</c>, <c>Words_20</c> to <c>Words_90</c>, <c>Words_100</c> to <c>Words_900</c>, the thousand and million
/// forms, <c>Words_Construct_3</c> to <c>Words_Construct_10</c> and <c>Words_And</c> (Khalil's list, 17 Oct 2026).
/// </summary>
public static class AmountWordResources
{
    extension(AmountWordParts)
    {
        /// <summary>The parts as the resource file holds them. Built once; the console registers the result as a singleton.</summary>
        public static AmountWordParts FromResources(IStringLocalizer<PhoneStrings> text)
        {
            ArgumentNullException.ThrowIfNull(text);
            string Word(string key) => text["Words_" + key].Value;
            static string Figure(int value) => value.ToString(CultureInfo.InvariantCulture);

            return new AmountWordParts(
                Units: [.. Enumerable.Range(1, AmountWordParts.UnitCount).Select(i => Word(Figure(i)))],
                Tens: [.. Enumerable.Range(2, AmountWordParts.TensCount).Select(i => Word(Figure(i * 10)))],
                Hundreds: [.. Enumerable.Range(1, AmountWordParts.HundredsCount).Select(i => Word(Figure(i * 100)))],
                Thousand: Word("Thousand"),
                TwoThousand: Word("TwoThousand"),
                Thousands: Word("Thousands"),
                Million: Word("Million"),
                TwoMillion: Word("TwoMillion"),
                Millions: Word("Millions"),
                Constructs: [.. Enumerable.Range(3, AmountWordParts.ConstructCount).Select(i => Word("Construct_" + Figure(i)))],
                And: Word("And"));
        }
    }
}
