extern alias AgentWeb;

using System.Text.RegularExpressions;
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
/// What the milestone 7 journeys share: a fresh browser context and a reset wallet and agent database per journey, the
/// phone's strings through the console's localizer, and helpers that act and read through <c>data-testid</c> only.
/// </summary>
public abstract partial class JourneyBase(JourneyHost host) : IAsyncLifetime
{
    protected const int U1 = 1;
    protected const int U2 = 2;
    protected const int U3 = 3;
    protected const int U5 = 5;
    protected const long U1Balance = 750_000;

    private IBrowserContext? context;

    protected JourneyHost Host => host;

    protected IStringLocalizer<PhoneStrings> Text => host.WebServices.GetRequiredService<IStringLocalizer<PhoneStrings>>();

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (host.SkipReason is not null)
            return;
        await host.ResetAsync();
        context = await host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1600, Height = 950 },

            // Milestone 9b: the copy buttons write to the clipboard, and the purchase journeys read it back.
            Permissions = ["clipboard-read", "clipboard-write"],
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (context is not null)
            await context.DisposeAsync();
    }

    /// <summary>Opens a console and waits until its circuit is interactive.</summary>
    protected async Task<IPage> OpenAsync(Uri address)
    {
        Assert.SkipWhen(host.SkipReason is not null, host.SkipReason ?? string.Empty);

        var page = await context!.NewPageAsync();
        await page.GotoAsync(address.ToString());
        await Expect(page.GetByTestId("phone-frame")).ToHaveAttributeAsync("data-interactive", "true");
        return page;
    }

    /// <summary>Types <paramref name="text"/> in the phone and waits for the user's message and the agent's first answer.</summary>
    protected static async Task SayAsync(IPage page, string text)
    {
        var messages = page.GetByTestId("chat-message");
        var before = await messages.CountAsync();
        var input = page.GetByTestId("phone-input");
        await input.FillAsync(text);
        await input.PressAsync("Enter");
        await Expect(messages.Nth(before + 1)).ToBeVisibleAsync();
    }

    /// <summary>What the page's clipboard holds now (the context grants clipboard-read).</summary>
    protected static async Task<string> ClipboardAsync(IPage page) => await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

    /// <summary>The agent's latest chat message.</summary>
    protected static ILocator LastAgentMessage(IPage page) => page.Locator("[data-testid=chat-message][data-role=agent]").Last;

    /// <summary>The outcome messages of one kind (<c>done</c>, <c>cancelled</c>, <c>pin</c>, ...).</summary>
    protected static ILocator Outcomes(IPage page, string outcome) => page.Locator($"[data-testid=outcome][data-outcome={outcome}]");

    /// <summary>The text of every element with <paramref name="testId"/>.</summary>
    protected static async Task<IReadOnlyList<string>> TextsAsync(IPage page, string testId)
        => await page.GetByTestId(testId).AllInnerTextsAsync();

    /// <summary>The digits of <paramref name="text"/> (ASCII and Arabic-Indic, both as ASCII), nothing else.</summary>
    protected static string DigitsOf(string? text)
        => NotADigit().Replace(Agent.Core.Text.ArabicDigits.ToAscii(text ?? string.Empty), string.Empty);

    /// <summary>Every audit row and every learning row of the agent database, flattened to their text columns.</summary>
    protected async Task<IReadOnlyList<string>> AgentRowsAsync()
    {
        await using var scope = host.WebServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var audit = await db.AuditEntries.AsNoTracking().ToListAsync(Token);
        var learning = await db.LearningEntries.AsNoTracking().ToListAsync(Token);
        return
        [
            .. audit.Select(a => string.Join('|', a.Event, a.FromState, a.ToState, a.Reason, a.Reference, a.Detail)),
            .. learning.Select(l => string.Join('|', l.Reason, l.Detail, l.MaskedText)),
        ];
    }

    /// <summary>How many audit rows of <paramref name="event"/> the agent database holds.</summary>
    protected async Task<int> AuditCountAsync(string @event)
    {
        await using var scope = host.WebServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.AuditEntries.CountAsync(a => a.Event == @event, Token);
    }

    /// <summary>Every learning row of the agent database as (reason, detail).</summary>
    protected async Task<IReadOnlyList<(string Reason, string? Detail)>> LearningRowsAsync()
    {
        await using var scope = host.WebServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return [.. (await db.LearningEntries.AsNoTracking().ToListAsync(Token)).Select(l => (l.Reason, l.Detail))];
    }

    /// <summary>
    /// Sets the wallet's fault switch through the fault panel of <paramref name="page"/> (milestone 8): the mode, the
    /// delay, once, Apply; then waits until the panel shows the state the wallet reports back.
    /// </summary>
    protected static async Task SetWalletFaultAsync(IPage page, string mode, int delayMs, bool once)
    {
        await page.GetByTestId("fault-wallet-mode").SelectOptionAsync(mode);
        await page.GetByTestId("fault-wallet-delay").FillAsync(delayMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await page.GetByTestId("fault-wallet-once").SetCheckedAsync(once);
        await page.GetByTestId("fault-wallet-apply").ClickAsync();
        var expected = mode is "error" or "none"
            ? new Regex("^" + Regex.Escape(mode) + "( |$)")
            : new Regex("^" + Regex.Escape(mode) + " " + delayMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (once ? "once" : "every call") + "$");
        await Expect(page.GetByTestId("fault-wallet-state")).ToHaveTextAsync(expected);
    }

    /// <summary>Arms a model fault (<c>unreachable</c> or <c>invalid_reply</c>) through the fault panel of <paramref name="page"/>.</summary>
    protected static async Task ArmModelFaultAsync(IPage page, string mode, bool once)
    {
        await page.GetByTestId("fault-model-mode").SelectOptionAsync(mode);
        await page.GetByTestId("fault-model-once").SetCheckedAsync(once);
        await page.GetByTestId("fault-model-apply").ClickAsync();
        await Expect(page.GetByTestId("fault-model-state")).ToHaveTextAsync(mode + " " + (once ? "once" : "every call"));
    }

    /// <summary>Takes <paramref name="amountIqd"/> from the console's user through the fault panel; waits for the done result.</summary>
    protected static async Task DebitAsync(IPage page, long amountIqd)
    {
        await page.GetByTestId("fault-debit-amount").FillAsync(amountIqd.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await page.GetByTestId("fault-debit-apply").ClickAsync();
        await Expect(page.GetByTestId("fault-debit-result")).ToHaveTextAsync(new Regex("^done "));
    }

    /// <summary>
    /// The end of every fault journey: the wallet's switch is back to none (a once fault cleared itself), and the
    /// panel's Clear all reads both switches back as <c>none</c>.
    /// </summary>
    protected async Task AssertFaultsClearedAsync(IPage page)
    {
        Assert.Equal("none", await host.WalletFaultModeAsync());
        await page.GetByTestId("fault-clear").ClickAsync();
        await Expect(page.GetByTestId("fault-wallet-state")).ToHaveTextAsync("none");
        await Expect(page.GetByTestId("fault-model-state")).ToHaveTextAsync("none");
        await Expect(page.GetByTestId("fault-error")).ToHaveCountAsync(0);
    }

    /// <summary>The text of <paramref name="key"/> with an amount, as the phone formats it.</summary>
    protected string WithAmount(string key, long amountIqd) => Text[key, PhoneFormat.Amount(amountIqd, Text)].Value;

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NotADigit();
}
