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

        Assert.Equal(ArabicDigits.ToArabicIndic("2026/10/19"), PhoneFormat.Date(instant));
    }
}
