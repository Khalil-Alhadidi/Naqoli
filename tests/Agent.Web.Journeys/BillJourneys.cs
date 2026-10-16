extern alias AgentWeb;

using Agent.Core.Audit;
using Agent.Core.Learning;
using Agent.Core.Pipeline;
using Agent.Core.Resolver;
using Agent.Core.Wallet;
using AgentWeb::Agent.Web.Services;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Milestone 8, bills on screen: a named provider straight to the card, two accounts at one biller as options, the
/// bill-or-store question, nothing due, part of a bill, and the duplicate warning. Every sentence is a development-set
/// case typed by id; every name and figure is read from the wallet or the resource file.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class BillJourneys(JourneyHost host) : JourneyBase(host)
{
    private static ILocator Questions(IPage page) => page.Locator("[data-testid=chat-message][data-kind=question]");

    private static async Task<IReadOnlyList<string?>> OptionValuesAsync(IPage page)
    {
        var values = new List<string?>();
        foreach (var option in await page.GetByTestId("question-option").AllAsync())
            values.Add(await option.GetAttributeAsync("data-value"));
        return values;
    }

    /// <summary>The name the card shows for a bill: its label, else the biller's name, as the wallet holds them.</summary>
    private static string CardName(WalletBill bill) => PhoneFormat.Digits(bill.Label ?? bill.BillerName);

    private async Task<WalletBill> BillAsync(int userId, string code) => (await Host.BillsAsync(userId)).Single(b => b.Code == code);

    private async Task ExpectBillCardAsync(IPage page, WalletBill bill, long feeIqd)
    {
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-display")).ToHaveTextAsync(CardName(bill));
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(bill.AmountDueIqd, Text));
        await Expect(page.GetByTestId("card-fee")).ToHaveTextAsync(PhoneFormat.Amount(feeIqd, Text));
        await Expect(page.GetByTestId("card-total")).ToHaveTextAsync(PhoneFormat.Amount(bill.AmountDueIqd + feeIqd, Text));
    }

    /// <summary>The bill fee, from the wallet's own settings (500 in the seed).</summary>
    private async Task<long> BillFeeAsync()
    {
        using var document = System.Text.Json.JsonDocument.Parse(await Host.Wallet.GetStringAsync("/settings", Token));
        return document.RootElement.GetProperty("billFeeIqd").GetInt64();
    }

    [Fact]
    public async Task NamedProviderGoesStraightToTheBillCard()
    {
        var page = await OpenAsync(Host.WebAddress);
        var bs4 = await BillAsync(U1, "BS4");
        var fee = await BillFeeAsync();

        await SayAsync(page, DevSet.Say("CLN-09"));

        // The bill-or-store rule (16 Oct 2026): the provider's name sends the internet bill straight to the card.
        await ExpectBillCardAsync(page, bs4, fee);
        await Expect(Questions(page)).ToHaveCountAsync(0);

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(0, (await BillAsync(U1, "BS4")).AmountDueIqd);
        Assert.Equal(U1Balance - bs4.AmountDueIqd - fee, await Host.BalanceAsync(U1));
        Assert.Equal(714_500, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task TwoAccountsAtOneBillerBecomeOptions()
    {
        var page = await OpenAsync(Host.WebAddress);
        var bs1 = await BillAsync(U1, "BS1");
        var fee = await BillFeeAsync();

        await SayAsync(page, DevSet.Say("AMB-07"));

        await Expect(Questions(page)).ToHaveCountAsync(1);
        await Expect(Questions(page)).ToHaveTextAsync(Text["Question_" + QuestionKinds.WhichBill].Value);
        await Expect(page.GetByTestId("question-option")).ToHaveCountAsync(2);
        Assert.Equal(["BS1", "BS2"], (await OptionValuesAsync(page)).Order(StringComparer.Ordinal));
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);

        await page.Locator("[data-testid=question-option][data-value=BS1]").ClickAsync();

        await ExpectBillCardAsync(page, bs1, fee);
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(43_750, Text));

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(0, (await BillAsync(U1, "BS1")).AmountDueIqd);
        Assert.Equal(705_750, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task InternetWithoutAProviderAsksBillOrStore()
    {
        var page = await OpenAsync(Host.UserWebAddress(U3));
        var bt3 = await BillAsync(U3, "BT3");
        var fee = await BillFeeAsync();
        var balance = await Host.BalanceAsync(U3);

        await SayAsync(page, DevSet.Say("CLN-25"));

        await Expect(Questions(page)).ToHaveCountAsync(1);
        await Expect(Questions(page)).ToHaveTextAsync(Text["Question_" + QuestionKinds.BillOrStore].Value);
        Assert.Equal(["BT3", Option.StoreInternet], await OptionValuesAsync(page));
        await Expect(page.Locator($"[data-testid=question-option][data-value='{Option.StoreInternet}']"))
            .ToHaveTextAsync(Text["Option_store_internet"].Value);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);

        await page.Locator("[data-testid=question-option][data-value=BT3]").ClickAsync();

        await ExpectBillCardAsync(page, bt3, fee);
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(65_000, Text));

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(0, (await BillAsync(U3, "BT3")).AmountDueIqd);
        Assert.Equal(balance - bt3.AmountDueIqd - fee, await Host.BalanceAsync(U3));
    }

    [Fact]
    public async Task NothingDueGivesNoCard()
    {
        var page = await OpenAsync(Host.UserWebAddress(U2));

        await SayAsync(page, DevSet.Say("ADD-05"));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_" + ReasonCodes.NothingDue].Value);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Equal(18_500, await Host.BalanceAsync(U2));
        Assert.Equal(0, await AuditCountAsync(AuditEvents.Created));
        Assert.Empty(await LearningRowsAsync());
    }

    [Fact]
    public async Task PartOfABillIsRefusedWithTheFullAmount()
    {
        var page = await OpenAsync(Host.WebAddress);
        var bs3 = await BillAsync(U1, "BS3");

        await SayAsync(page, DevSet.Say("ADD-04"));

        // D9: a bill is paid in full; the figure is the bill's amount due from the wallet, never the amount typed.
        await Expect(LastAgentMessage(page)).ToHaveTextAsync(WithAmount("Refusal_" + ReasonCodes.BillsPaidInFull, bs3.AmountDueIqd));
        Assert.Equal(9_250, bs3.AmountDueIqd);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
        Assert.Equal(0, await AuditCountAsync(AuditEvents.Created));
        var learning = Assert.Single(await LearningRowsAsync());
        Assert.Equal(LearningLogRule.PartOfBill, learning.Reason);
    }

    [Fact]
    public async Task RepeatedRequestCarriesTheDuplicateWarning()
    {
        var page = await OpenAsync(Host.WebAddress);
        var historyBefore = (await Host.HistoryAsync(U1)).Count;

        await SayAsync(page, DevSet.Say("FLT-05"));
        await page.GetByTestId("card-confirm").ClickAsync();
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);

        await SayAsync(page, DevSet.Say("FLT-05", 1));

        // The same amount to the same person a few minutes ago: the new card says so, and waits for its own tap.
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-warning")).ToHaveTextAsync(Text["Card_Warning_" + CardBuilder.RepeatWarning].Value);

        await page.GetByTestId("card-cancel").ClickAsync();

        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "cancelled");
        Assert.Equal(historyBefore + 1, (await Host.HistoryAsync(U1)).Count);
        Assert.Equal(724_000, await Host.BalanceAsync(U1));
    }
}
