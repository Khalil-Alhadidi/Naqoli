using Agent.Core.Tests.Wallet;
using Agent.Core.Text;

namespace Agent.Core.Tests.Text;

/// <summary>Rule 1's normalisation. Arabic is built from <c>\u</c> escapes; no Arabic literal appears in C#.</summary>
public class ArabicTextTests
{
    private const char Alef = '\u0627';                // ARABIC LETTER ALEF
    private const char AlefHamzaAbove = '\u0623';      // ARABIC LETTER ALEF WITH HAMZA ABOVE
    private const char AlefHamzaBelow = '\u0625';      // ARABIC LETTER ALEF WITH HAMZA BELOW
    private const char AlefMadda = '\u0622';           // ARABIC LETTER ALEF WITH MADDA ABOVE
    private const char AlefMaksura = '\u0649';         // ARABIC LETTER ALEF MAKSURA
    private const char Yeh = '\u064A';                 // ARABIC LETTER YEH
    private const char TehMarbuta = '\u0629';          // ARABIC LETTER TEH MARBUTA
    private const char Heh = '\u0647';                 // ARABIC LETTER HEH
    private const char Tatweel = '\u0640';             // ARABIC TATWEEL
    private const char Fatha = '\u064E';               // ARABIC FATHA
    private const char Shadda = '\u0651';              // ARABIC SHADDA
    private const char Sukun = '\u0652';               // ARABIC SUKUN
    private const char SuperscriptAlef = '\u0670';     // ARABIC LETTER SUPERSCRIPT ALEF
    private const char Lam = '\u0644';                 // ARABIC LETTER LAM
    private const char Meem = '\u0645';                // ARABIC LETTER MEEM
    private const char Hah = '\u062D';                 // ARABIC LETTER HAH
    private const char Dal = '\u062F';                 // ARABIC LETTER DAL
    private const char Ain = '\u0639';                 // ARABIC LETTER AIN
    private const char Beh = '\u0628';                 // ARABIC LETTER BEH
    private const char Khah = '\u062E';                // ARABIC LETTER KHAH
    private const char Teh = '\u062A';                 // ARABIC LETTER TEH
    private const char Waw = '\u0648';                 // ARABIC LETTER WAW

    // "ahmad" without and with hamza: alef, hah, meem, dal.
    private static readonly string Ahmad = new([Alef, Hah, Meem, Dal]);

    [Theory]
    [InlineData(AlefHamzaAbove)]
    [InlineData(AlefHamzaBelow)]
    [InlineData(AlefMadda)]
    public void HamzaAndMaddaFormsEqualAlef(char form)
        => Assert.Equal(Ahmad, ArabicText.Normalize(form + Ahmad[1..]));

    [Fact]
    public void TehMarbutaEqualsHeh()
        => Assert.Equal(ArabicText.Normalize(new string([Meem, Dal, Heh])), ArabicText.Normalize(new string([Meem, Dal, TehMarbuta])));

    [Fact]
    public void AlefMaksuraEqualsYeh()
        => Assert.Equal(ArabicText.Normalize(new string([Ain, Lam, Yeh])), ArabicText.Normalize(new string([Ain, Lam, AlefMaksura])));

    [Fact]
    public void TatweelAndHarakatAreRemoved()
    {
        var decorated = new string([Alef, Fatha, Hah, Tatweel, Tatweel, Meem, Shadda, Sukun, SuperscriptAlef, Dal]);

        Assert.Equal(Ahmad, ArabicText.Normalize(decorated));
    }

    [Fact]
    public void ArabicIndicDigitsBecomeAscii()
    {
        Assert.Equal("0123456789", ArabicText.Normalize("\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669"));
        Assert.Equal("0123456789", ArabicText.Normalize("\u06F0\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6\u06F7\u06F8\u06F9"));
    }

    [Fact]
    public void WhitespaceIsTrimmedAndCollapsed()
        => Assert.Equal(Ahmad + " " + Ahmad, ArabicText.Normalize("  " + Ahmad + " \t  " + Ahmad + "\n"));

    [Fact]
    public void KinshipVariantMapsToItsCanonicalForm()
    {
        // alef khah yeh ("my brother", short form) and alef khah waw yeh teh-marbuta (the canonical form).
        var shortForm = new string([Alef, Khah, Yeh]);
        var canonical = new string([Alef, Khah, Waw, Yeh, TehMarbuta]);

        Assert.Equal(ArabicText.Normalize(canonical), ArabicText.Normalize(shortForm));
        Assert.Equal(ArabicText.Normalize(canonical + " " + Ahmad), ArabicText.Normalize(shortForm + " " + Ahmad));
        Assert.True(ArabicText.IsKinship(ArabicText.Normalize(shortForm)));
    }

    [Fact]
    public void SisterVariantMapsToItsCanonicalForm()
    {
        // alef khah teh yeh teh-marbuta and alef khah teh yeh.
        Assert.Equal(
            ArabicText.Normalize(new string([Alef, Khah, Teh, Yeh])),
            ArabicText.Normalize(new string([Alef, Khah, Teh, Yeh, TehMarbuta])));
    }

    [Fact]
    public void TitleWordsAreTitles()
    {
        Assert.True(ArabicText.IsTitle(new string([Alef, Beh, Waw])));   // abu
        Assert.True(ArabicText.IsTitle(new string([Alef, Meem])));       // umm
        Assert.False(ArabicText.IsTitle(Ahmad));
        Assert.False(ArabicText.IsKinship(Ahmad));
    }

    [Fact]
    public void LeadingAlIsStrippedOnlyAsAWholeNamePrefix()
    {
        var al = new string([Alef, Lam]);
        var word = new string([Meem, Waw, Lam, Dal, Heh]);

        Assert.Equal(word, ArabicText.StripLeadingAl(al + word));
        Assert.Equal(al + Dal, ArabicText.StripLeadingAl(al + Dal));            // one letter left: kept
        Assert.Equal(Ahmad + " " + al + word, ArabicText.StripLeadingAl(Ahmad + " " + al + word));  // not inside
        Assert.Equal(word, ArabicText.Key(al + word));
    }

    [Fact]
    public void KeyOfAllahIsTheTwoLetterRemainder()
        => Assert.Equal(new string([Lam, Heh]), ArabicText.Key(new string([Alef, Lam, Lam, Heh])));

    [Fact]
    public void KeyRemovesSpacesAndNormalises()
    {
        var abd = new string([Ain, Beh, Dal]);
        var allah = new string([Alef, Lam, Lam, Heh]);

        Assert.Equal(ArabicText.Key(abd + allah), ArabicText.Key(abd + " " + allah));
        Assert.Equal(Ahmad, ArabicText.Key(AlefHamzaAbove + Ahmad[1..]));
    }

    [Fact]
    public void WordsSplitTheNormalisedText()
        => Assert.Equal([Ahmad, Ahmad], ArabicText.Words("  " + Ahmad + "   " + AlefHamzaAbove + Ahmad[1..]));

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]
    [InlineData("abc", "ab", 1)]
    [InlineData("ab", "abc", 1)]
    [InlineData("kitten", "sitting", 3)]
    public void EditDistanceIsLevenshtein(string a, string b, int expected)
        => Assert.Equal(expected, ArabicText.EditDistance(a, b));

    [Fact]
    public void DigitsConvertBothWays()
    {
        Assert.Equal("50000", ArabicDigits.ToAscii("\u0665\u0660\u0660\u0660\u0660"));
        Assert.Equal("\u0665\u0660,\u0660\u0660\u0660", ArabicDigits.ToArabicIndic("50,000"));
    }

    [Fact]
    public void FormatCharactersAreDropped()
    {
        // A seed name with a right-to-left override in front, a zero-width joiner and a zero-width space inside.
        var name = SeedFacts.Contact("S01").SavedName;
        Assert.True(name.Length >= 3);
        var dirty = "\u202E" + name[..1] + "\u200D" + name[1..2] + "\u200B" + name[2..];

        Assert.Equal(ArabicText.Key(name), ArabicText.Key(dirty));
        Assert.Equal(ArabicText.Normalize(name), ArabicText.Normalize(dirty));
    }
}
