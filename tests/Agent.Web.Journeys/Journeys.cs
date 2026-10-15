extern alias AgentWeb;

using System.Globalization;
using Agent.Core.Audit;
using Agent.Core.Data;
using AgentWeb::Agent.Web.Resources;
using AgentWeb::Agent.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// The first slice on screen (milestone 4): the Hammoudi request typed in the phone, the card built by code, Confirm
/// moving the money once, the timer killing the button, and a double tap. Headless Chromium against the in-process
/// wallet and console, with the scripted parser.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class Journeys(JourneyHost host) : IAsyncLifetime
{
    private const int DemoUserId = 1;
    private const long DemoBalance = 750_000;

    private IBrowserContext? context;

    /// <summary>CLN-01 of the development set, as Khalil wrote it: fifty thousand to S01 (the oracle returns its parse).</summary>
    private static string TransferSentence => DevSet.Say("CLN-01");

    private IStringLocalizer<PhoneStrings> Text => host.WebServices.GetRequiredService<IStringLocalizer<PhoneStrings>>();

    public async ValueTask InitializeAsync()
    {
        if (host.SkipReason is not null)
            return;
        await host.ResetAsync();
        context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1600, Height = 950 } });
    }

    public async ValueTask DisposeAsync()
    {
        if (context is not null)
            await context.DisposeAsync();
    }

    /// <summary>Opens the console, waits until the circuit is live, and types the transfer sentence.</summary>
    private async Task<IPage> OpenAndAskAsync(Uri address)
    {
        Assert.SkipWhen(host.SkipReason is not null, host.SkipReason ?? string.Empty);

        var page = await context!.NewPageAsync();
        await page.GotoAsync(address.ToString());
        await Expect(page.GetByTestId("phone-frame")).ToHaveAttributeAsync("data-interactive", "true");

        var input = page.GetByTestId("phone-input");
        await input.FillAsync(TransferSentence);
        await input.PressAsync("Enter");
        await Expect(page.GetByTestId("card")).ToBeVisibleAsync();
        return page;
    }

    [Fact]
    public async Task HammoudiHappyPath()
    {
        var page = await OpenAndAskAsync(host.WebAddress);
        var historyBefore = (await host.HistoryAsync(DemoUserId)).Count;

        // The card's figures, built by code from the quote, in Arabic-Indic digits.
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(50_000, Text));
        await Expect(page.GetByTestId("card-fee")).ToHaveTextAsync(PhoneFormat.Amount(1_000, Text));
        await Expect(page.GetByTestId("card-total")).ToHaveTextAsync(PhoneFormat.Amount(51_000, Text));
        await Expect(page.GetByTestId("card-balance-after")).ToHaveTextAsync(PhoneFormat.Amount(699_000, Text));

        // The names, read from the wallet, shown as text.
        var contacts = await host.ContactsAsync(DemoUserId);
        var hammoudi = contacts.Contacts.Single(c => c.Code == "S01");
        var alias = contacts.Aliases.Single(a => a.TargetAccountId == hammoudi.TargetAccountId);
        await Expect(page.GetByTestId("card-display")).ToHaveTextAsync(alias.Text);
        await Expect(page.GetByTestId("card-registered")).ToHaveTextAsync(alias.RegisteredName ?? hammoudi.RegisteredName!);

        await page.GetByTestId("card-confirm").ClickAsync();

        var done = page.Locator("[data-testid=outcome][data-outcome=done]");
        await Expect(done).ToHaveCountAsync(1);
        var history = await host.HistoryAsync(DemoUserId);
        Assert.Equal(historyBefore + 1, history.Count);
        var reference = history[0].Reference;
        Assert.Equal("ZC-0000000087", reference);
        await Expect(done).ToContainTextAsync(PhoneFormat.Digits(reference));

        // The console: ASCII figures, the new balance and the new history row.
        await Expect(page.GetByTestId("wallet-balance")).ToContainTextAsync(699_000.ToString("N0", CultureInfo.InvariantCulture));
        await Expect(page.Locator($"[data-testid=wallet-history-row][data-reference='{reference}']")).ToHaveCountAsync(1);
        Assert.Equal(699_000, await host.BalanceAsync(DemoUserId));

        // The trace shows every zone the request went through.
        foreach (var zone in new[] { "user", "model", "code", "write" })
            await Expect(page.Locator($"[data-testid=trace-entry][data-zone={zone}]").First).ToBeVisibleAsync();

        // D20: the phone is right to left; the console left to right.
        await Expect(page.GetByTestId("phone-frame")).ToHaveAttributeAsync("dir", "rtl");
        await Expect(page.GetByTestId("console")).ToHaveAttributeAsync("dir", "ltr");
    }

    [Fact]
    public async Task TimerKillsTheButton()
    {
        var page = await OpenAndAskAsync(host.ShortTimerWebAddress);
        var historyBefore = (await host.HistoryAsync(DemoUserId)).Count;
        var confirm = page.GetByTestId("card-confirm");
        await Expect(confirm).ToBeEnabledAsync();

        // CardTimerSeconds is 4 on this console: the button goes when the countdown ends.
        await Expect(confirm).ToBeDisabledAsync(new LocatorAssertionsToBeDisabledOptions { Timeout = 8_000 });
        await Expect(page.Locator("[data-testid=outcome][data-outcome=expired]").First).ToBeVisibleAsync();

        Assert.Equal(DemoBalance, await host.BalanceAsync(DemoUserId));
        Assert.Equal(historyBefore, (await host.HistoryAsync(DemoUserId)).Count);
    }

    [Fact]
    public async Task DoubleTapExecutesOnce()
    {
        var page = await OpenAndAskAsync(host.WebAddress);
        var historyBefore = (await host.HistoryAsync(DemoUserId)).Count;
        var confirm = page.GetByTestId("card-confirm");

        // Two clicks as fast as Playwright sends them, both forced (no wait for the button to be enabled), so the second
        // lands whatever the first did to the button.
        await Task.WhenAll(
            confirm.ClickAsync(new LocatorClickOptions { Force = true }),
            confirm.ClickAsync(new LocatorClickOptions { Force = true }));

        var done = page.Locator("[data-testid=outcome][data-outcome=done]");
        await Expect(done).ToHaveCountAsync(1);

        // Give a second call, had one reached the server, time to answer; there is still only one done.
        await page.WaitForTimeoutAsync(1_000);
        await Expect(done).ToHaveCountAsync(1);

        var history = await host.HistoryAsync(DemoUserId);
        Assert.Equal(historyBefore + 1, history.Count);
        Assert.Equal(699_000, await host.BalanceAsync(DemoUserId));
        await Expect(page.Locator($"[data-testid=wallet-history-row][data-reference='{history[0].Reference}']")).ToHaveCountAsync(1);

        await using var scope = host.WebServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.Event == AuditEvents.Done, TestContext.Current.CancellationToken));
    }
}
