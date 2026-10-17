extern alias AgentWeb;

using Agent.Core.Audit;
using Agent.Core.Pipeline;
using Agent.Core.Text;
using AgentWeb::Agent.Web.Services;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Milestone 7, the request types on screen: a typed number, an account number, an unknown name, a short balance, a
/// cap, and a name two contacts share (D3: the options, then the tap that gives the card). Each types a development-set case by id; the console's scripted parser is the oracle over the same set.
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
    public async Task AmbiguousNameOffersOptionsAndTheTapGivesTheCard()
    {
        var page = await OpenAsync(Host.WebAddress);
        var historyBefore = (await Host.HistoryAsync(U1)).Count;

        await SayAsync(page, DevSet.Say("AMB-02", 0));

        // D3: one question with exactly the two contacts as options, and no card.
        var question = page.Locator("[data-testid=chat-message][data-kind=question]");
        await Expect(question).ToHaveCountAsync(1);
        // The options are shown under the question message (QuestionPanel), not inside it.
        var options = page.GetByTestId("question-option");
        await Expect(options).ToHaveCountAsync(2);
        var values = new List<string?>();
        foreach (var option in await options.AllAsync())
            values.Add(await option.GetAttributeAsync("data-value"));
        Assert.Equal(["S10", "S11"], values.Order(StringComparer.Ordinal));
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);

        // Each option's small line labels the last amount sent to that contact as the wallet holds it, or says there
        // was no previous transfer.
        var wallet = await Host.ContactsAsync(U1);
        foreach (var code in values)
        {
            var listed = wallet.Contacts.Single(c => c.Code == code);
            var expected = listed.LastSentAmountIqd is { } last
                ? WithAmount("Option_LastSent", last)
                : Text["Option_NoPreviousTransfer"].Value;
            await Expect(page.Locator($"[data-testid=question-option][data-value={code}]").GetByTestId("option-last-sent"))
                .ToHaveTextAsync(expected);
        }

        await page.Locator("[data-testid=question-option][data-value=S10]").ClickAsync();

        // The card names S10 as the wallet holds it; nothing has moved yet.
        var contact = (await Host.ContactsAsync(U1)).Contacts.Single(c => c.Code == "S10");
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-display")).ToHaveTextAsync(contact.SavedName);
        await Expect(page.GetByTestId("card-registered")).ToHaveTextAsync(contact.RegisteredName!);
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(50_000, Text));
        // The amount in words under the figure, composed from the resource file's words (Khalil's list, 17 Oct 2026).
        await Expect(page.GetByTestId("card-amount-words"))
            .ToHaveTextAsync(Text["Card_AmountWords", AmountWords.Compose(50_000, AmountWordParts.FromResources(Text))!].Value);
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
        Assert.Equal(historyBefore, (await Host.HistoryAsync(U1)).Count);

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        var history = await Host.HistoryAsync(U1);
        Assert.Equal(historyBefore + 1, history.Count);
        Assert.Equal(contact.Phone, history[0].CounterpartyPhone);
        Assert.Equal(699_000, await Host.BalanceAsync(U1));
        Assert.Equal(1, await AuditCountAsync(AuditEvents.Done));
    }

    [Fact]
    public async Task NoneOfTheseOnWhichContactRefusesRecipientNotFound()
    {
        var page = await OpenAsync(Host.WebAddress);

        await SayAsync(page, DevSet.Say("AMB-02", 0));

        // The two options, then the final option, "none of these" (17 Oct 2026).
        await Expect(page.GetByTestId("question-option")).ToHaveCountAsync(2);
        var decline = page.GetByTestId("question-decline");
        await Expect(decline).ToHaveCountAsync(1);
        await Expect(decline).ToHaveAttributeAsync("data-value", Option.NoneOfThese);
        await Expect(decline).ToHaveTextAsync(Text["Option_NoneOfThese"].Value);

        await decline.ClickAsync();

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_recipient_not_found"].Value);
        await Expect(page.GetByTestId("question-option")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("question-decline")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Contains(await LearningRowsAsync(), r => r.Reason == "recipient_not_found");
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task NoneOfTheseOnDidYouMeanRefusesRecipientNotFound()
    {
        var page = await OpenAsync(Host.WebAddress);

        await SayAsync(page, DevSet.Say("CLN-03-v1", 0));

        await Expect(page.GetByTestId("question-option")).ToHaveCountAsync(1);
        var decline = page.GetByTestId("question-decline");
        await Expect(decline).ToHaveTextAsync(Text["Option_NoneOfThese"].Value);

        await decline.ClickAsync();

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_recipient_not_found"].Value);
        await Expect(page.GetByTestId("question-option")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Contains(await LearningRowsAsync(), r => r.Reason == "recipient_not_found");
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task CancelOnATypedQuestionClearsThePendingRequest()
    {
        var page = await OpenAsync(Host.WebAddress);

        await SayAsync(page, DevSet.Say("AMB-06", 0));

        // A typed question has no options: the panel shows only the final option, "cancel".
        await Expect(page.GetByTestId("question-option")).ToHaveCountAsync(0);
        var decline = page.GetByTestId("question-decline");
        await Expect(decline).ToHaveCountAsync(1);
        await Expect(decline).ToHaveAttributeAsync("data-value", Option.CancelQuestion);
        await Expect(decline).ToHaveTextAsync(Text["Option_CancelQuestion"].Value);

        await decline.ClickAsync();

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_question_cancelled"].Value);
        await Expect(page.GetByTestId("question-decline")).ToHaveCountAsync(0);

        // Nothing is pending any more, so the amount alone completes nothing.
        await SayAsync(page, DevSet.Say("AMB-06", 1));

        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Equal(0, await AuditCountAsync(AuditEvents.Created));
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task AllMyMoneyAsksTheAmountAndTheTypedAnswerGivesTheCard()
    {
        var page = await OpenAsync(Host.WebAddress);
        var historyBefore = (await Host.HistoryAsync(U1)).Count;

        await SayAsync(page, DevSet.Say("ADD-20", 0));

        // ADD-20: "all my money" is a transfer with the amount missing; the agent asks, and never reads the balance in.
        var question = page.Locator("[data-testid=chat-message][data-kind=question]");
        await Expect(question).ToHaveCountAsync(1);
        await Expect(question).ToHaveTextAsync(Text["Question_which_amount"].Value);
        await Expect(page.GetByTestId("question-option")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("question-decline")).ToHaveCountAsync(1);

        await SayAsync(page, DevSet.Say("ADD-20", 1));

        var contact = (await Host.ContactsAsync(U1)).Contacts.Single(c => c.Code == "S14");
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-display")).ToHaveTextAsync(contact.SavedName);
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(650_000, Text));
        await Expect(page.GetByTestId("card-amount-words"))
            .ToHaveTextAsync(Text["Card_AmountWords", AmountWords.Compose(650_000, AmountWordParts.FromResources(Text))!].Value);

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(99_000, await Host.BalanceAsync(U1));
        var history = await Host.HistoryAsync(U1);
        Assert.Equal(historyBefore + 1, history.Count);
        Assert.Equal(contact.Phone, history[0].CounterpartyPhone);
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
