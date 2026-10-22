extern alias AgentWeb;

using Agent.Core.Text;
using AgentWeb::Agent.Web.Services;

namespace Agent.Core.Tests.Web;

/// <summary>Figures inside the phone frame (D20): Arabic-Indic digits, Iraq dates.</summary>
public class PhoneFormatTests
{
    [Fact]
    public void ANumberHasArabicIndicDigitsAndACommaBetweenTheThousands()
    {
        // Khalil, 22 Oct 2026: the comma that hangs down (U+002C), not the raised ARABIC THOUSANDS SEPARATOR (U+066C).
        var number = PhoneFormat.Number(1_250_000);

        Assert.Equal(ArabicDigits.ToArabicIndic("1,250,000"), number);
        Assert.DoesNotContain('٬', number);
        Assert.Equal(',', PhoneFormat.ThousandsSeparator);
    }

    [Fact]
    public void ADateIsShownAsTheIraqDateInArabicIndicDigits()
    {
        // 23:30 UTC is 02:30 the next day in Iraq (UTC+3).
        var instant = new DateTimeOffset(2026, 10, 18, 23, 30, 0, TimeSpan.Zero);

        // dd/MM/yyyy (security review R2 of milestone 9b).
        Assert.Equal(ArabicDigits.ToArabicIndic("19/10/2026"), PhoneFormat.Date(instant));
    }

    [Fact]
    public void TheStatusBarTimeIsTheIraqTimeInArabicIndicDigits()
    {
        // UI pass task 7: 23:05 UTC is 02:05 in Iraq (UTC+3), 24-hour, two digits each.
        var instant = new DateTimeOffset(2026, 10, 18, 23, 5, 0, TimeSpan.Zero);

        Assert.Equal(ArabicDigits.ToArabicIndic("02:05"), PhoneFormat.Time(instant));
    }
}
