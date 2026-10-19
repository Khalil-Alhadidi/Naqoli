extern alias AgentWeb;

using Agent.Core.Text;
using AgentWeb::Agent.Web.Services;

namespace Agent.Core.Tests.Web;

/// <summary>Figures inside the phone frame (D20): Arabic-Indic digits, Iraq dates.</summary>
public class PhoneFormatTests
{
    [Fact]
    public void ADateIsShownAsTheIraqDateInArabicIndicDigits()
    {
        // 23:30 UTC is 02:30 the next day in Iraq (UTC+3).
        var instant = new DateTimeOffset(2026, 10, 18, 23, 30, 0, TimeSpan.Zero);

        // dd/MM/yyyy (security review R2 of milestone 9b).
        Assert.Equal(ArabicDigits.ToArabicIndic("19/10/2026"), PhoneFormat.Date(instant));
    }
}
