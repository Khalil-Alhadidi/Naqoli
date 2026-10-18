extern alias AgentWeb;

using System.Globalization;
using Agent.Core.Audit;
using AgentWeb::Agent.Web.Services;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Milestone 7, the balance, consent, settings, PIN and kill switch on screen, with the console's own components (the
/// log, the settings) read through their <c>data-testid</c>s.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class ConsoleJourneys(JourneyHost host) : JourneyBase(host)
{
    /// <summary>The Amount column of a <c>log-row</c> (At, User, Event, Pending, From → To, Reason, Reference, Amount, Detail).</summary>
    private const int LogAmountColumn = 7;

    [Fact]
    public async Task BalanceAnswersAfterConsentAndTheLogKeepsNoFigure()
    {
        var page = await OpenAsync(Host.WebAddress);

        await SayAsync(page, DevSet.Say("CLN-31"));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(WithAmount("Reply_balance", U1Balance));
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);

        // D22: the figure goes to the screen only; the audit row and the trace hold none.
        await page.GetByTestId("menu-log").ClickAsync();
        var row = page.Locator("[data-testid=log-row][data-event=balance_shown]");
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row.Locator("td").Nth(LogAmountColumn)).ToHaveTextAsync(string.Empty);
        Assert.All(await TextsAsync(page, "trace-entry"), entry => Assert.DoesNotContain("750", DigitsOf(entry), StringComparison.Ordinal));
    }

    /// <summary>
    /// The first request's consent gate, kept as the fallback (milestone 7, post-tag): the balance typed under the open
    /// screen's button shows the gate's consent screen again, and the tap then answers the balance.
    /// </summary>
    [Fact]
    public async Task ConsentScreenComesBeforeTheBalance()
    {
        var page = await OpenAsync(Host.UserWebAddress(U5));
        var balance = await Host.BalanceAsync(U5);
        Assert.False((await Host.UserAsync(U5)).ConsentGiven);

        await SayAsync(page, DevSet.Say("CLN-31"));

        await Expect(LastAgentMessage(page)).ToContainTextAsync(Text["Reply_consent"].Value);
        await Expect(page.GetByTestId("consent-button")).ToBeVisibleAsync();
        var figures = new[] { PhoneFormat.Amount(balance, Text), PhoneFormat.Number(balance), PhoneFormat.Console(balance), balance.ToString(CultureInfo.InvariantCulture) };
        Assert.All(await TextsAsync(page, "chat-message"), message => Assert.All(figures, f => Assert.DoesNotContain(f, message, StringComparison.Ordinal)));

        await page.GetByTestId("consent-button").ClickAsync();

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(WithAmount("Reply_balance", balance));
        await Expect(page.GetByTestId("consent-button")).ToHaveCountAsync(0);
        Assert.True((await Host.UserAsync(U5)).ConsentGiven);
    }

    /// <summary>The open check (milestone 7, post-tag; Khalil, 18 Oct 2026): the consent screen before any typing.</summary>
    [Fact]
    public async Task AFreshUserSeesTheConsentScreenBeforeTyping()
    {
        var page = await OpenAsync(Host.UserWebAddress(U5));
        var balance = await Host.BalanceAsync(U5);

        // Nothing typed: the phone already shows the consent message with its button.
        await Expect(page.Locator("[data-testid=chat-message][data-role=user]")).ToHaveCountAsync(0);
        await Expect(LastAgentMessage(page)).ToContainTextAsync(Text["Reply_consent"].Value);
        await Expect(page.GetByTestId("consent-button")).ToBeVisibleAsync();
        Assert.False((await Host.UserAsync(U5)).ConsentGiven);

        await page.GetByTestId("consent-button").ClickAsync();

        await Expect(page.GetByTestId("consent-button")).ToHaveCountAsync(0);
        Assert.True((await Host.UserAsync(U5)).ConsentGiven);

        // The balance is answered straight away; the consent screen was shown, and recorded, once.
        await SayAsync(page, DevSet.Say("CLN-31"));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(WithAmount("Reply_balance", balance));
        await Expect(page.GetByTestId("consent-button")).ToHaveCountAsync(0);
        Assert.Equal(1, await AuditCountAsync(AuditEvents.ConsentShown));
    }

    /// <summary>
    /// The console's Reset control (milestone 7, post-tag; Khalil, 18 Oct 2026): the wallet back to the seed with every
    /// user not consented, the open card closed, and the phone started over on the consent screen.
    /// </summary>
    [Fact]
    public async Task ResetClearsConsentAndThePhoneStartsAgain()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CLN-01"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");

        await page.GetByTestId("reset-wallet").ClickAsync();

        await Expect(page.GetByTestId("reset-result")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("reset-error")).ToHaveCountAsync(0);
        Assert.False((await Host.UserAsync(U1)).ConsentGiven);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("chat-message")).ToHaveCountAsync(1);
        await Expect(LastAgentMessage(page)).ToContainTextAsync(Text["Reply_consent"].Value);
        await Expect(page.GetByTestId("consent-button")).ToBeVisibleAsync();

        await page.GetByTestId("consent-button").ClickAsync();

        await Expect(page.GetByTestId("consent-button")).ToHaveCountAsync(0);
        Assert.True((await Host.UserAsync(U1)).ConsentGiven);

        await SayAsync(page, DevSet.Say("CLN-01"));

        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
    }

    [Fact]
    public async Task AChangedCapTakesEffectWithoutARestart()
    {
        var page = await OpenAsync(Host.WebAddress);
        const string cap = "PerTransactionCapIqd";

        await page.GetByTestId("menu-settings").ClickAsync();
        await page.Locator($"[data-testid=setting-input][data-name={cap}]").FillAsync("40000");
        await page.Locator($"[data-testid=setting-save][data-name={cap}]").ClickAsync();
        await Expect(page.Locator($"[data-testid=setting-value][data-name={cap}]")).ToHaveTextAsync(PhoneFormat.Console(40_000));

        await SayAsync(page, DevSet.Say("CLN-01"));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(WithAmount("Refusal_over_per_transaction_cap", 40_000));
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);

        await page.Locator($"[data-testid=setting-reset][data-name={cap}]").ClickAsync();
        await Expect(page.Locator($"[data-testid=setting-value][data-name={cap}]")).ToHaveTextAsync(PhoneFormat.Console(5_000_000));

        await SayAsync(page, DevSet.Say("CLN-01"));

        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.Locator("[data-testid=setting-row][data-name='Wallet:PinRequiredAboveIqd']"))
            .ToContainTextAsync(PhoneFormat.Console(1_000_000));
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task PinFieldShowsForALargeAmountAndThePinIsNeverLogged()
    {
        var page = await OpenAsync(Host.UserWebAddress(U3));
        var balance = await Host.BalanceAsync(U3);
        var pin = SeedPins.PinOf("U3");
        var wrong = SeedPins.WrongPinOf("U3");

        await SayAsync(page, DevSet.Say("RED-09"));

        var pinField = page.GetByTestId("card-pin");
        await Expect(pinField).ToBeVisibleAsync();

        await pinField.FillAsync(wrong);
        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "pin")).ToHaveTextAsync(Text["Outcome_pin_wrong", PhoneFormat.Number(2)].Value);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        Assert.Equal(balance, await Host.BalanceAsync(U3));

        await pinField.FillAsync(pin);
        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(balance - 1_501_000, await Host.BalanceAsync(U3));

        // Neither PIN, in either digit form, is in the chat, the trace, the audit log or the learning log.
        var forms = new[] { pin, wrong, PhoneFormat.Digits(pin), PhoneFormat.Digits(wrong) };
        var seen = new List<string>();
        seen.AddRange(await TextsAsync(page, "chat-message"));
        seen.AddRange(await TextsAsync(page, "trace-entry"));
        seen.AddRange(await AgentRowsAsync());
        Assert.True(seen.Count > 0);
        Assert.False(
            seen.Any(text => forms.Any(form => text.Contains(form, StringComparison.Ordinal))),
            "A PIN appears in the chat, the trace, the audit log or the learning log.");
    }

    [Fact]
    public async Task KillSwitchStopsEverything()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CLN-01"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");

        await page.GetByTestId("menu-settings").ClickAsync();
        var killSwitch = page.GetByTestId("kill-switch");
        await killSwitch.ClickAsync();
        await Expect(killSwitch).ToHaveAttributeAsync("data-on", "true");

        // Confirm on the live card: not done, nothing taken.
        await page.GetByTestId("card-confirm").ClickAsync();
        await Expect(Outcomes(page, "not_done")).ToHaveCountAsync(1);
        await Expect(Outcomes(page, "not_done")).ToHaveTextAsync(Text["Outcome_not_done", Text["Refusal_nothing_taken_short"].Value].Value);
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));

        // A new request gets no card; the balance question still answers.
        await SayAsync(page, DevSet.Say("CLN-02"));
        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_not_done_nothing_taken"].Value);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "notdone");
        Assert.Equal(1, await AuditCountAsync(AuditEvents.Created));

        await SayAsync(page, DevSet.Say("CLN-31"));
        await Expect(LastAgentMessage(page)).ToHaveTextAsync(WithAmount("Reply_balance", U1Balance));

        await killSwitch.ClickAsync();
        await Expect(killSwitch).ToHaveAttributeAsync("data-on", "false");

        await SayAsync(page, DevSet.Say("CLN-02"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(100_000, Text));
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));

        // Both flips are audit rows.
        await page.GetByTestId("menu-log").ClickAsync();
        await Expect(page.Locator("[data-testid=log-row][data-event=kill_switch]")).ToHaveCountAsync(2);
    }
}
