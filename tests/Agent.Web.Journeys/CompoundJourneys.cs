extern alias AgentWeb;

using Agent.Core.Learning;
using Agent.Core.Pipeline;
using AgentWeb::Agent.Web.Services;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Milestone 9c, compound requests on screen (D7, rules 21 and 22), one journey per behaviour, on the oracle: the cards
/// come in the order spoken, each with its own tap; the next card only after the previous is finished; a cancel, an
/// expiry or a failure on one still leads to the next; a new request cancels the rest; the parts beyond the cap are named
/// under the last prepared card. Every sentence, figure and option is read from the development set, every text from
/// the resource file.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class CompoundJourneys(JourneyHost host) : JourneyBase(host)
{
    /// <summary>The wallet fault's delay, as the fault journeys set it; an error answers at once whatever the delay.</summary>
    private const int FaultDelayMs = 12_000;

    /// <summary>Until the 4-second card has expired and the countdown asked the server.</summary>
    private static readonly LocatorAssertionsToBeVisibleOptions UntilTheTimerEnds = new() { Timeout = 10_000 };

    /// <summary>The card messages of the chat: one per card issued.</summary>
    private static ILocator CardMessages(IPage page) => page.Locator("[data-testid=chat-message][data-kind=card]");

    /// <summary>The card panel shown, once it is live with <paramref name="amountIqd"/> on it.</summary>
    private async Task<ILocator> LiveCardAsync(IPage page, long amountIqd)
    {
        var card = page.GetByTestId("card");
        await Expect(card).ToHaveAttributeAsync("data-state", "live");
        await Expect(card.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(amountIqd, Text));
        return card;
    }

    [Fact]
    public async Task TwoCardsComeInTheOrderSpokenEachWithItsOwnTap()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CMP-01"));

        var first = await LiveCardAsync(page, DevSet.CardFigure("CMP-01", 0, "amount"));
        await first.GetByTestId("card-confirm").ClickAsync();
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);

        // The purchase card comes only now, under its own message.
        var second = await LiveCardAsync(page, DevSet.CardFigure("CMP-01", 1, "amount"));
        await second.GetByTestId("card-cancel").ClickAsync();

        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "cancelled");
        await Expect(Outcomes(page, "cancelled")).ToHaveCountAsync(1);
        await Expect(CardMessages(page)).ToHaveCountAsync(2);
        Assert.Equal(DevSet.FinalBalance("CMP-01"), await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task AnExpiredCardStillLeadsToTheNext()
    {
        var page = await OpenAsync(Host.ShortTimerWebAddress);
        await SayAsync(page, DevSet.Say("CMP-03"));
        await LiveCardAsync(page, DevSet.CardFigure("CMP-03", 0, "amount"));

        // CardTimerSeconds is 4 here: card 1 expires on its own, and card 2 follows, from the full balance.
        await Expect(Outcomes(page, "expired").First).ToBeVisibleAsync(UntilTheTimerEnds);
        var second = await LiveCardAsync(page, DevSet.CardFigure("CMP-03", 1, "amount"));
        await Expect(second.GetByTestId("card-balance-after")).ToHaveTextAsync(PhoneFormat.Amount(DevSet.CardFigure("CMP-03", 1, "balance_after"), Text));

        await second.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(DevSet.FinalBalance("CMP-03"), await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task AQuestionOnTheFirstPartIsAskedInItsTurnAndTheSecondWaits()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CMP-05"));

        // which_bill with the case's two options, and no card yet: the transfer waits for its turn.
        var options = page.GetByTestId("question-option");
        await Expect(options).ToHaveCountAsync(DevSet.AskOptions("CMP-05", 0).Count);
        var values = new List<string?>();
        foreach (var option in await options.AllAsync())
            values.Add(await option.GetAttributeAsync("data-value"));
        Assert.Equal(DevSet.AskOptions("CMP-05", 0).Order(StringComparer.Ordinal), values.Order(StringComparer.Ordinal));
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);

        await page.Locator($"[data-testid=question-option][data-value={DevSet.TapValue("CMP-05", 1)}]").ClickAsync();
        var bill = await LiveCardAsync(page, DevSet.CardFigure("CMP-05", 0, "amount"));
        await bill.GetByTestId("card-confirm").ClickAsync();
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);

        var transfer = await LiveCardAsync(page, DevSet.CardFigure("CMP-05", 1, "amount"));
        await Expect(transfer.GetByTestId("card-balance-after")).ToHaveTextAsync(PhoneFormat.Amount(DevSet.CardFigure("CMP-05", 1, "balance_after"), Text));
        await transfer.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(2);
        Assert.Equal(DevSet.FinalBalance("CMP-05"), await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task FourRequestsGiveThreeCardsAndTheReplyNamesTheFourth()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CMP-07"));

        for (var card = 0; card < 3; card++)
        {
            var shown = await LiveCardAsync(page, DevSet.CardFigure("CMP-07", card, "amount"));
            await shown.GetByTestId("card-confirm").ClickAsync();
            await Expect(Outcomes(page, "done")).ToHaveCountAsync(card + 1);
        }

        // Rule 22: Khalil's line, then the line that names the fourth part in the user's own words; no fourth card.
        var agent = page.Locator("[data-testid=chat-message][data-role=agent]");
        await Expect(agent.Last).ToHaveTextAsync(Text["Refusal_not_prepared", DevSet.NotPrepared("CMP-07")].Value);
        await Expect(agent.Nth(await agent.CountAsync() - 2)).ToHaveTextAsync(Text["Refusal_" + ReasonCodes.MaxThreeRequests].Value);
        await Expect(CardMessages(page)).ToHaveCountAsync(3);
        Assert.Contains(await LearningRowsAsync(), r => r.Reason == LearningLogRule.MoreThanThreeRequests);
        Assert.Equal(DevSet.FinalBalance("CMP-07"), await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task TheBalanceIsAnsweredBeforeTheCardOfTheSecondPart()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CMP-09"));

        // In the order spoken: the balance line, then the transfer's card message.
        var agent = page.Locator("[data-testid=chat-message][data-role=agent]");
        await Expect(agent).ToHaveCountAsync(2);
        await Expect(agent.First).ToHaveTextAsync(WithAmount("Reply_balance", DevSet.ReplyAmount("CMP-09", 0)));
        await Expect(agent.Nth(1)).ToHaveAttributeAsync("data-kind", "card");

        var card = await LiveCardAsync(page, DevSet.CardFigure("CMP-09", 0, "amount"));
        await card.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(DevSet.FinalBalance("CMP-09"), await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task ACancelledCardStillLeadsToTheNext()
    {
        var page = await OpenAsync(Host.WebAddress);
        var balance = await Host.BalanceAsync(U1);
        await SayAsync(page, DevSet.Say("CMP-01"));

        var first = await LiveCardAsync(page, DevSet.CardFigure("CMP-01", 0, "amount"));
        await first.GetByTestId("card-cancel").ClickAsync();

        await Expect(Outcomes(page, "cancelled")).ToHaveCountAsync(1);
        await LiveCardAsync(page, DevSet.CardFigure("CMP-01", 1, "amount"));
        await Expect(CardMessages(page)).ToHaveCountAsync(2);
        Assert.Equal(balance, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task AFailedCardStillLeadsToTheNext()
    {
        var page = await OpenAsync(Host.WebAddress);
        var balance = await Host.BalanceAsync(U1);
        var historyBefore = (await Host.HistoryAsync(U1)).Count;

        // The wallet's error, once, on the first payment: card 1 is not done, nothing taken, and card 2 still follows.
        await SetWalletFaultAsync(page, "error", FaultDelayMs, once: true);
        await SayAsync(page, DevSet.Say("CMP-03"));
        var first = await LiveCardAsync(page, DevSet.CardFigure("CMP-03", 0, "amount"));
        await first.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "not_done")).ToHaveTextAsync(Text["Outcome_not_done", Text["Refusal_nothing_taken_short"].Value].Value);
        await LiveCardAsync(page, DevSet.CardFigure("CMP-03", 1, "amount"));
        await Expect(CardMessages(page)).ToHaveCountAsync(2);
        Assert.Equal(balance, await Host.BalanceAsync(U1));
        Assert.Equal(historyBefore, (await Host.HistoryAsync(U1)).Count);
        await AssertFaultsClearedAsync(page);
    }

    [Fact]
    public async Task ANewMessageCancelsTheRest()
    {
        var page = await OpenAsync(Host.WebAddress);
        await SayAsync(page, DevSet.Say("CMP-01"));
        await LiveCardAsync(page, DevSet.CardFigure("CMP-01", 0, "amount"));

        // D7: a new request while card 1 waits cancels it and drops the purchase still queued.
        await SayAsync(page, DevSet.Say("CLN-01"));

        await Expect(Outcomes(page, "cancelled")).ToHaveCountAsync(1);
        var card = await LiveCardAsync(page, DevSet.CardFigure("CLN-01", 0, "amount"));
        await card.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "done");
        await Expect(CardMessages(page)).ToHaveCountAsync(2);
        Assert.Equal(DevSet.FinalBalance("CLN-01"), await Host.BalanceAsync(U1));
    }
}
