using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// The UI pass's screenshot helper (Khalil, 21 Oct 2026): not a journey, a camera. Skipped unless
/// <c>ZAINCASH_SCREENSHOTS</c> is <c>1</c>; it captures U1's console at rest, with CLN-01's live card, and on the
/// settings, log and wallet data components, at 1,440 x 900, into <c>ZAINCASH_SCREENSHOTS_DIR</c> (default
/// <c>screenshots</c> under the current directory). It asserts only that the files exist.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class Screenshots(JourneyHost host) : IAsyncLifetime
{
    private const string EnabledVariable = "ZAINCASH_SCREENSHOTS";
    private const string DirectoryVariable = "ZAINCASH_SCREENSHOTS_DIR";

    private static readonly string[] Files =
        ["console-at-rest.png", "card-live.png", "phone-card.png", "settings.png", "log.png", "wallet-data.png"];

    private IBrowserContext? context;

    private static bool Enabled => Environment.GetEnvironmentVariable(EnabledVariable) == "1";

    public async ValueTask InitializeAsync()
    {
        if (!Enabled || host.SkipReason is not null)
            return;
        await host.ResetAsync();
        context = await host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (context is not null)
            await context.DisposeAsync();
    }

    [Fact]
    public async Task CaptureTheConsoleAndThePhone()
    {
        Assert.SkipUnless(Enabled, $"Screenshots are taken only when {EnabledVariable} is 1");
        Assert.SkipWhen(host.SkipReason is not null, host.SkipReason ?? string.Empty);

        var folder = Path.GetFullPath(Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } dir ? dir : "screenshots");
        Directory.CreateDirectory(folder);
        string At(string file) => Path.Combine(folder, file);

        var page = await context!.NewPageAsync();
        await page.GotoAsync(host.WebAddress.ToString());
        await Expect(page.GetByTestId("phone-frame")).ToHaveAttributeAsync("data-interactive", "true");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = At("console-at-rest.png"), FullPage = true });

        var input = page.GetByTestId("phone-input");
        await input.FillAsync(DevSet.Say("CLN-01"));
        await input.PressAsync("Enter");
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = At("card-live.png"), FullPage = true });
        await page.GetByTestId("phone-frame").ScreenshotAsync(new LocatorScreenshotOptions { Path = At("phone-card.png") });

        await page.GetByTestId("menu-settings").ClickAsync();
        await Expect(page.Locator("[data-testid=setting-row]").First).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = At("settings.png"), FullPage = true });

        await page.GetByTestId("menu-log").ClickAsync();
        await Expect(page.GetByTestId("log-refresh")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = At("log.png"), FullPage = true });

        await page.GetByTestId("menu-wallet").ClickAsync();
        await Expect(page.GetByTestId("wallet-balance")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = At("wallet-data.png"), FullPage = true });

        foreach (var file in Files)
            Assert.True(File.Exists(At(file)), $"{file} was not written to {folder}");
    }
}
