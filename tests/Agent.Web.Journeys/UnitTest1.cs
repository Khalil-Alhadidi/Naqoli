namespace Agent.Web.Journeys;

public class UnitTest1
{
    [Fact]
    public void PlaywrightIsReferenced()
    {
        Assert.NotNull(typeof(Microsoft.Playwright.IPlaywright));
    }
}
