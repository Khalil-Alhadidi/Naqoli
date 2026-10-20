extern alias AgentWeb;

using Agent.Core.Pipeline;
using Agent.Core.Wallet;
using AgentWeb::Agent.Web.Services;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Person or bill on screen (Khalil, 20 Oct 2026): BS2's label is saved as an alias of S04 through the phone's directory,
/// so CLN-10's name is both a person and a bill. The agent asks <c>person_or_bill</c>; the bill tap gives the bill card,
/// the person tap the transfer path. The sentence is typed by case id; every name and figure comes from the wallet or the
/// resource file.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class PersonOrBillJourneys(JourneyHost host) : JourneyBase(host)
{
    private const string Cln10 = "CLN-10";

    private static ILocator Questions(IPage page) => page.Locator("[data-testid=chat-message][data-kind=question]");

    private static ILocator OptionOf(IPage page, string value) => page.Locator($"[data-testid=question-option][data-value='{value}']");

    private async Task<WalletBill> Bs2Async() => (await Host.BillsAsync(U1)).Single(b => b.Code == "BS2");

    /// <summary>Opens the phone, saves BS2's label as an alias of S04 in the directory, and says CLN-10.</summary>
    private async Task<IPage> AskAsync()
    {
        var page = await OpenAsync(Host.WebAddress);
        var label = (await Bs2Async()).Label!;

        await OpenDirectoryAsync(page);
        await AddInDirectoryAsync(page, label, "S04");
        await Expect(page.GetByTestId("directory-result")).ToHaveAttributeAsync("data-key", "Reply_alias_saved");
        await page.GetByTestId("phone-directory-back").ClickAsync();
        await Expect(page.GetByTestId("phone-input")).ToBeVisibleAsync();

        await SayAsync(page, DevSet.Say(Cln10));
        await Expect(Questions(page)).ToHaveCountAsync(1);
        return page;
    }

    [Fact]
    public async Task ANameThatIsAContactAndABillAsksPersonOrBill()
    {
        var page = await AskAsync();
        var bs2 = await Bs2Async();
        var s04 = (await Host.ContactsAsync(U1)).Contacts.Single(c => c.Code == "S04");

        await Expect(Questions(page)).ToHaveTextAsync(Text["Question_" + QuestionKinds.PersonOrBill].Value);
        await Expect(page.GetByTestId("question-option")).ToHaveCountAsync(2);
        await Expect(OptionOf(page, "S04")).ToContainTextAsync(s04.SavedName);
        await Expect(OptionOf(page, "BS2")).ToContainTextAsync(bs2.BillerName + " " + bs2.Label);
        await Expect(OptionOf(page, "BS2")).ToContainTextAsync(PhoneFormat.Amount(bs2.AmountDueIqd, Text));

        // The person option keeps a contact option's last-sent line; the bill option has none.
        await Expect(OptionOf(page, "S04").GetByTestId("option-last-sent")).ToHaveCountAsync(1);
        await Expect(OptionOf(page, "BS2").GetByTestId("option-last-sent")).ToHaveCountAsync(0);

        await Expect(page.GetByTestId("question-decline")).ToHaveAttributeAsync("data-value", Option.CancelQuestion);
        await Expect(page.GetByTestId("question-decline")).ToHaveTextAsync(Text["Option_CancelQuestion"].Value);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TappingTheBillGivesTheBillCard()
    {
        var page = await AskAsync();
        var bs2 = await Bs2Async();

        await OptionOf(page, "BS2").ClickAsync();

        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-display")).ToHaveTextAsync(PhoneFormat.Digits(bs2.Label!));
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(bs2.AmountDueIqd, Text));

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        Assert.Equal(0, (await Bs2Async()).AmountDueIqd);
    }

    [Fact]
    public async Task TappingThePersonGivesTheTransferPath()
    {
        var page = await AskAsync();
        var due = (await Bs2Async()).AmountDueIqd;

        await OptionOf(page, "S04").ClickAsync();

        // CLN-10 names no amount: the transfer asks for it.
        await Expect(Questions(page)).ToHaveCountAsync(2);
        await Expect(Questions(page).Last).ToHaveTextAsync(Text["Question_" + QuestionKinds.WhichAmount].Value);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Equal(due, (await Bs2Async()).AmountDueIqd);
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
    }
}
