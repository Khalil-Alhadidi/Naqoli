extern alias AgentWeb;

using AgentWeb::Agent.Web.Services;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Milestone 7, the request types on screen: a typed number, an account number, an unknown name, a short balance and a
/// cap. Each types a development-set case by id; the console's scripted parser is the oracle over the same set.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class RequestJourneys(JourneyHost host) : JourneyBase(host)
{
    /// <summary>ADD-01's number: seed account X01, which no contact of U1 points to and U1 never paid.</summary>
    private const string TypedNumber = "07805550190";

    [Fact]
    public async Task TypedNumberShowsTheRegisteredNameAndFirstTime()
    {
        var page = await OpenAsync(Host.WebAddress);
        var historyBefore = (await Host.HistoryAsync(U1)).Count;

        await SayAsync(page, DevSet.Say("ADD-01"));

        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-display")).ToHaveTextAsync(PhoneFormat.Digits(TypedNumber));
        var account = await Host.AccountByPhoneAsync(TypedNumber);
        await Expect(page.GetByTestId("card-registered")).ToHaveTextAsync(account.RegisteredName!);
        await Expect(page.GetByTestId("card-first-time")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(20_000, Text));

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        var history = await Host.HistoryAsync(U1);
        Assert.Equal(historyBefore + 1, history.Count);
        Assert.Equal(TypedNumber, history[0].CounterpartyPhone);
        Assert.Equal(U1Balance - 21_000, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task AccountNumberIsRefusedAndMasked()
    {
        var page = await OpenAsync(Host.WebAddress);
        var typed = DevSet.Say("ADD-03");

        await SayAsync(page, typed);

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_invalid_number"].Value);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));

        // D21 and rule 5: the account number's digits reach no trace entry and no row of the agent database.
        var digits = DigitsOf(typed);
        Assert.Equal(14, digits.Length);
        Assert.All(await TextsAsync(page, "trace-entry"), entry => Assert.DoesNotContain(digits, DigitsOf(entry), StringComparison.Ordinal));
        Assert.All(await AgentRowsAsync(), row => Assert.DoesNotContain(digits, DigitsOf(row), StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownContactIsRefused()
    {
        var page = await OpenAsync(Host.WebAddress);

        await SayAsync(page, DevSet.Say("ADD-02"));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_recipient_not_found"].Value);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task InsufficientBalanceIsRefused()
    {
        var page = await OpenAsync(Host.UserWebAddress(U2));
        var balance = await Host.BalanceAsync(U2);
        Assert.Equal(18_500, balance);

        await SayAsync(page, DevSet.Say("NOGO-11"));

        // The key gives no figure: the template's fixed text, then the shortfall the wallet's quote gave.
        var template = Text["Refusal_insufficient_balance"].Value;
        var fixedPart = template[..template.IndexOf("{0}", StringComparison.Ordinal)];
        await Expect(LastAgentMessage(page)).ToContainTextAsync(fixedPart.Trim());
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Equal(18_500, await Host.BalanceAsync(U2));
    }

    [Fact]
    public async Task OverCapIsRefusedWithTheCap()
    {
        var page = await OpenAsync(Host.UserWebAddress(U3));
        var balance = await Host.BalanceAsync(U3);

        await SayAsync(page, DevSet.Say("NOGO-04"));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(WithAmount("Refusal_over_per_transaction_cap", 5_000_000));
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Equal(balance, await Host.BalanceAsync(U3));
    }
}
