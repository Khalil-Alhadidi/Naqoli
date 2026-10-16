extern alias AgentWeb;

using Agent.Core.Audit;
using Agent.Core.Learning;
using Agent.Core.Pipeline;
using AgentWeb::Agent.Web.Services;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Milestone 8, the six faults on screen (<c>docs/matching-rules.md</c>, "Faults the switch must offer"): each is set
/// through the fault panel of the console under test (its <c>data-testid</c>s), never through the host's own client,
/// so the panel itself is exercised; each journey ends with the panel reading both switches as <c>none</c>. The two
/// timeouts show the Checking state and resolve by themselves under the same key (rule 6).
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class FaultJourneys(JourneyHost host) : JourneyBase(host)
{
    /// <summary>The wallet's fault delay: longer than the console's wallet timeout (10 s), so the agent gives up first.</summary>
    private const int FaultDelayMs = 12_000;

    /// <summary>Until the Confirm's wallet call times out (10 s) and the card shows Checking.</summary>
    private static readonly LocatorAssertionsToBeVisibleOptions UntilChecking = new() { Timeout = 20_000 };

    /// <summary>Until the automatic settle (every CheckingRetrySeconds, 5 s) resolves the card, with no tap.</summary>
    private static readonly LocatorAssertionsToHaveCountOptions UntilSettled = new() { Timeout = 15_000 };

    private static ILocator TraceEntries(IPage page, string step)
        => page.Locator("[data-testid=trace-entry]").Filter(new LocatorFilterOptions { HasText = step });

    /// <summary>
    /// Journeys 7 and 8: a timeout while the payment runs. The card shows Checking, never failed; the card panel settles
    /// it by itself under the same key; the money moves once.
    /// </summary>
    private async Task TimeoutShowsCheckingThenDoneAsync(string mode)
    {
        var page = await OpenAsync(Host.WebAddress);
        var historyBefore = (await Host.HistoryAsync(U1)).Count;
        await SetWalletFaultAsync(page, mode, FaultDelayMs, once: true);

        await SayAsync(page, DevSet.Say("CLN-23"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "checking").First).ToBeVisibleAsync(UntilChecking);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "checking");
        await Expect(page.GetByTestId("card-check-again")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("card-checking")).ToHaveTextAsync(Text["Card_Checking"].Value);
        await Expect(Outcomes(page, "not_done")).ToHaveCountAsync(0);

        // No tap: the automatic settle resends under the same key and the card resolves to done.
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1, UntilSettled);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "done");
        await Expect(Outcomes(page, "not_done")).ToHaveCountAsync(0);
        Assert.True(await TraceEntries(page, "lookup_by_key").CountAsync() >= 1, "The settle did not trace the lookup by key.");
        await Expect(TraceEntries(page, "settle_outcome:done")).ToHaveCountAsync(1);

        Assert.Equal(historyBefore + 1, (await Host.HistoryAsync(U1)).Count);
        Assert.Equal(740_250, await Host.BalanceAsync(U1));
        Assert.Equal(1, await AuditCountAsync(AuditEvents.Done));
        await AssertFaultsClearedAsync(page);
    }

    [Fact]
    public Task TimeoutAfterExecuteShowsCheckingThenDone() => TimeoutShowsCheckingThenDoneAsync("timeoutAfterExecute");

    [Fact]
    public Task TimeoutBeforeExecuteShowsCheckingThenResolves() => TimeoutShowsCheckingThenDoneAsync("timeoutBeforeExecute");

    [Fact]
    public async Task WalletErrorEndsTheCardNotDone()
    {
        var page = await OpenAsync(Host.UserWebAddress(U3));
        var balance = await Host.BalanceAsync(U3);
        var historyBefore = (await Host.HistoryAsync(U3)).Count;
        await SetWalletFaultAsync(page, "error", FaultDelayMs, once: true);

        await SayAsync(page, DevSet.Say("FLT-03"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await page.GetByTestId("card-confirm").ClickAsync();

        // The wallet's injected error wrote nothing: a refusal, not an unknown outcome.
        var expected = Text["Outcome_not_done", Text["Refusal_" + ReasonCodes.NotDoneNothingTaken].Value].Value;
        await Expect(Outcomes(page, "not_done")).ToHaveTextAsync(expected);
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "notdone");
        await Expect(page.GetByTestId("card-try-again")).ToBeVisibleAsync();
        Assert.Equal(historyBefore, (await Host.HistoryAsync(U3)).Count);
        Assert.Equal(balance, await Host.BalanceAsync(U3));
        await AssertFaultsClearedAsync(page);
    }

    [Fact]
    public async Task ModelUnreachableShowsTheHandoverMessage()
    {
        var page = await OpenAsync(Host.WebAddress);
        await ArmModelFaultAsync(page, "unreachable", once: true);

        await SayAsync(page, DevSet.Say("CLN-01"));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_" + ReasonCodes.ModelUnavailable].Value);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("fault-model-state")).ToHaveTextAsync("none");

        // The once fault has cleared: the same sentence now gets its card.
        await SayAsync(page, DevSet.Say("CLN-01"));

        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
        await AssertFaultsClearedAsync(page);
    }

    [Fact]
    public async Task ModelInvalidReplyIsNotUnderstood()
    {
        var page = await OpenAsync(Host.WebAddress);
        await ArmModelFaultAsync(page, "invalid_reply", once: true);

        await SayAsync(page, DevSet.Say("CLN-01"));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_" + ReasonCodes.NotUnderstood].Value);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        var learning = Assert.Single(await LearningRowsAsync());
        Assert.Equal((LearningLogRule.ModelInvalidReply, FaultingParser.InjectedProblem), learning);
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
        await AssertFaultsClearedAsync(page);
    }

    [Fact]
    public async Task ExternalDebitWhileTheCardWaitsIsCaughtByTheRecheck()
    {
        var page = await OpenAsync(Host.UserWebAddress(U2));
        var historyBefore = await Host.HistoryAsync(U2);

        await SayAsync(page, DevSet.Say("FLT-08"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(15_000, Text));

        // Money leaves the wallet from outside while the card waits (POST /admin/debit through the panel).
        await DebitAsync(page, 5_000);

        await page.GetByTestId("card-confirm").ClickAsync();

        // The Confirm recheck sees the lower balance: 16,000 to pay, 13,500 left, so 2,500 short; nothing is sent.
        var expected = Text["Outcome_not_done", WithAmount("Refusal_" + ReasonCodes.InsufficientBalance, 2_500)].Value;
        await Expect(Outcomes(page, "not_done")).ToHaveTextAsync(expected);
        Assert.Equal(13_500, await Host.BalanceAsync(U2));

        // The wallet data panel does not reload after a panel debit (a known gap): the wallet's history is read instead.
        var history = await Host.HistoryAsync(U2);
        Assert.Equal(historyBefore.Count + 1, history.Count);
        var added = Assert.Single(history, h => historyBefore.All(b => b.TransactionId != h.TransactionId));
        Assert.Equal(("externalDebit", "done", 5_000L), (added.Type, added.Status, added.AmountIqd));
        await AssertFaultsClearedAsync(page);
    }
}
