namespace Agent.Web.Journeys;

public class PlaywrightReferenceTests
{
    [Fact]
    public void PlaywrightIsReferenced()
    {
        Assert.NotNull(typeof(Microsoft.Playwright.IPlaywright));
    }
}
