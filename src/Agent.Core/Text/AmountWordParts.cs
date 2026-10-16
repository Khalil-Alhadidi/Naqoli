namespace Agent.Core.Text;

/// <summary>
/// The words the amount on the card is written with (Khalil's list, 17 Oct 2026). <c>Agent.Core</c> holds no Arabic:
/// the console builds the table once from its resource file.
/// </summary>
/// <param name="Units">The words for 1 to 19, in order (19 entries).</param>
/// <param name="Tens">The words for 20, 30 ... 90, in order (8 entries).</param>
/// <param name="Hundreds">The words for 100, 200 ... 900, in order (9 entries).</param>
/// <param name="Thousand">1,000, and the word after a counted group from 11 up.</param>
/// <param name="TwoThousand">2,000.</param>
/// <param name="Thousands">The plural after a construct form, for 3,000 to 10,000.</param>
/// <param name="Million">1,000,000, and the word after a counted group from 11 up.</param>
/// <param name="TwoMillion">2,000,000.</param>
/// <param name="Millions">The plural after a construct form, for 3 to 10 million.</param>
/// <param name="Constructs">The short counting forms for 3 to 10, used before the plurals (8 entries).</param>
/// <param name="And">The joiner, written against the following word with no space.</param>
public sealed record AmountWordParts(
    IReadOnlyList<string> Units,
    IReadOnlyList<string> Tens,
    IReadOnlyList<string> Hundreds,
    string Thousand,
    string TwoThousand,
    string Thousands,
    string Million,
    string TwoMillion,
    string Millions,
    IReadOnlyList<string> Constructs,
    string And)
{
    /// <summary>The entries <see cref="Units"/> holds: 1 to 19.</summary>
    public const int UnitCount = 19;

    /// <summary>The entries <see cref="Tens"/> holds: 20 to 90.</summary>
    public const int TensCount = 8;

    /// <summary>The entries <see cref="Hundreds"/> holds: 100 to 900.</summary>
    public const int HundredsCount = 9;

    /// <summary>The entries <see cref="Constructs"/> holds: 3 to 10.</summary>
    public const int ConstructCount = 8;
}
