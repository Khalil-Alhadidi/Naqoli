extern alias AgentWeb;

using Agent.Core.Audit;
using Agent.Core.Pipeline;
using AgentWeb::Agent.Web.Services;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Milestone 7, a live card on screen: D6 (a typed yes, a typed no, a new request, an unclear message) and D19 (Try again
/// after the timer, Send again after a payment). U1's console; the timer journey uses the 4-second console.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class CardJourneys(JourneyHost host) : JourneyBase(host)
{
    /// <summary>A text the oracle does not know, so the parser reads it as unclear.</summary>
    private const string UnclearText = "TODO: not a request";

    private static readonly LocatorAssertionsToBeVisibleOptions UntilTheTimerEnds = new() { Timeout = 10_000 };

    [Fact]
    public async Task TypedYesPointsToConfirm()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CLN-16"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");

        // CLN-16's own second say, a yes: it points to the button and confirms nothing (D6).
        await SayAsync(page, DevSet.Say("CLN-16", 1));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_points_to_confirm"].Value);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));

        await page.GetByTestId("card-confirm").ClickAsync();
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(U1Balance - 51_000, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task TypedNoCancelsTheCard()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CLN-01"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");

        await SayAsync(page, PhoneWords.Split(Text["Word_no"].Value)[0]);

        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "cancelled");
        await Expect(Outcomes(page, "cancelled")).ToHaveTextAsync(Text["Refusal_card_cancelled"].Value);
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task NewRequestCancelsTheCardAndStartsFresh()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CLN-01"));
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(50_000, Text));

        await SayAsync(page, DevSet.Say("CLN-02"));

        // The first card is cancelled; the only card shown is the new one, live, for 100,000.
        await Expect(Outcomes(page, "cancelled")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(100_000, Text));
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        Assert.Equal(2, await AuditCountAsync(AuditEvents.Created));
        Assert.Equal(1, await AuditCountAsync(AuditEvents.Cancelled));
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task UnclearTextLeavesTheCard()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CLN-01"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");

        await SayAsync(page, UnclearText);

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_not_understood"].Value);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(Outcomes(page, "cancelled")).ToHaveCountAsync(0);

        await page.GetByTestId("card-confirm").ClickAsync();
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(U1Balance - 51_000, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task InjectionTypedWhileACardWaitsCancelsTheCardAndMovesNothing()
    {
        var page = await OpenAsync(Host.WebAddress);
        var historyBefore = (await Host.HistoryAsync(U1)).Count;
        await SayAsync(page, DevSet.Say("ADD-06", 0));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");

        // ADD-06's injection: all the balance to a typed number. D6 stands (Khalil, 16 Oct 2026): a new request cancels
        // the waiting card even when the new request is refused. Words can stop money, never move it.
        await SayAsync(page, DevSet.Say("ADD-06", 1));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_" + ReasonCodes.NumberHasNoWallet].Value);
        await Expect(Outcomes(page, "cancelled")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "cancelled");
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(0);
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
        Assert.Equal(historyBefore, (await Host.HistoryAsync(U1)).Count);
        Assert.Equal(1, await AuditCountAsync(AuditEvents.Created));
        Assert.Equal(1, await AuditCountAsync(AuditEvents.Cancelled));
        Assert.Equal(0, await AuditCountAsync(AuditEvents.Executing));
        Assert.Equal(0, await AuditCountAsync(AuditEvents.Done));

        // As ADD-06's key: number_has_no_wallet is not logged, so no learning row.
        Assert.Empty(await LearningRowsAsync());
    }

    [Fact]
    public async Task TryAgainAfterExpiry()
    {
        var page = await OpenAsync(Host.ShortTimerWebAddress);
        var historyBefore = (await Host.HistoryAsync(U1)).Count;
        await SayAsync(page, DevSet.Say("CLN-01"));

        // CardTimerSeconds is 4 here: the card expires, and only then is Try again offered (D19).
        await Expect(Outcomes(page, "expired").First).ToBeVisibleAsync(UntilTheTimerEnds);
        var tryAgain = page.GetByTestId("card-try-again");
        await Expect(tryAgain).ToBeVisibleAsync();
        await Expect(page.GetByTestId("card-send-again")).ToHaveCountAsync(0);

        await tryAgain.ClickAsync();

        // A new record: a second created row, a live card with its own timer, and nothing moved yet.
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-timer")).ToBeVisibleAsync();
        Assert.Equal(2, await AuditCountAsync(AuditEvents.Created));
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(historyBefore + 1, (await Host.HistoryAsync(U1)).Count);
        Assert.Equal(U1Balance - 51_000, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task SendAgainAfterDone()
    {
        var page = await OpenAsync(Host.WebAddress);
        var historyBefore = (await Host.HistoryAsync(U1)).Count;
        await SayAsync(page, DevSet.Say("CLN-01"));
        await page.GetByTestId("card-confirm").ClickAsync();
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("card-try-again")).ToHaveCountAsync(0);

        await page.GetByTestId("card-send-again").ClickAsync();

        // The new card says the same amount was just sent to this person; nothing moves until its own Confirm.
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-warning")).ToHaveTextAsync(Text["Card_Warning_" + CardBuilder.RepeatWarning].Value);

        await page.GetByTestId("card-cancel").ClickAsync();

        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "cancelled");
        Assert.Equal(historyBefore + 1, (await Host.HistoryAsync(U1)).Count);
        Assert.Equal(U1Balance - 51_000, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task ThePhoneHasAStatusBarAndTheCardShowsTheTimerByConfirm()
    {
        // UI pass task 7: the status bar sits inside the phone frame, and the countdown is in the action block beside
        // Confirm (decision 8, option B).
        var page = await OpenAsync(Host.WebAddress);
        await Expect(page.GetByTestId("phone-frame").GetByTestId("phone-status-bar")).ToBeVisibleAsync();

        await SayAsync(page, DevSet.Say("CLN-01"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-timer")).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.querySelector('[data-testid=card-timer]').parentElement === document.querySelector('[data-testid=card-confirm]').parentElement"),
            "card-timer and card-confirm do not share one parent element");
    }
    /// <summary>
    /// Khalil, 22 Oct 2026: the phone's screen follows the chat. Each new message and each new card leaves the screen
    /// scrolled to its end, once the chat is taller than the screen, without the user moving the scrollbar.
    /// </summary>
    [Fact]
    public async Task TheScreenScrollsToTheNewestMessageAndCard()
    {
        var page = await OpenAsync(Host.WebAddress);
        var screen = page.GetByTestId("phone-screen");

        // Four cards and their outcomes: more than one screen of chat.
        for (var round = 1; round <= 4; round++)
        {
            await SayAsync(page, DevSet.Say("CLN-01"));
            await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
            await AssertScrolledToTheEndAsync(screen);

            await page.GetByTestId("card-confirm").ClickAsync();
            await Expect(Outcomes(page, "done")).ToHaveCountAsync(round);
            await AssertScrolledToTheEndAsync(screen);
        }

        Assert.True(await screen.EvaluateAsync<bool>("s => s.scrollHeight > s.clientHeight"), "the chat is not taller than the screen, so the journey proves nothing");
    }

    /// <summary>Waits up to two seconds for the screen to be scrolled to its end (within one pixel).</summary>
    private static async Task AssertScrolledToTheEndAsync(ILocator screen)
    {
        const string atTheEnd = "s => Math.abs(s.scrollHeight - s.clientHeight - s.scrollTop) <= 1";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await screen.EvaluateAsync<bool>(atTheEnd))
                return;
            await Task.Delay(100, Token);
        }

        var position = await screen.EvaluateAsync<string>("s => `scrollTop ${s.scrollTop}, clientHeight ${s.clientHeight}, scrollHeight ${s.scrollHeight}`");
        Assert.Fail($"the phone screen is not scrolled to its end: {position}");
    }
}
