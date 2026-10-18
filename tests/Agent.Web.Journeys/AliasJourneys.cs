using Agent.Core.Audit;
using Agent.Core.Wallet;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Milestone 9a, aliases on screen: the offer after a payment that needed a question (rule 18), saved only on the tap;
/// the saved alias going straight to the card; no offer without a question; a near spelling of a saved alias as an
/// option; the phone's directory and the console's Aliases panel listing, adding and deleting; a duplicate refused.
/// U1 on the oracle; every name and count from the wallet, the dev set or the resource file.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class AliasJourneys(JourneyHost host) : JourneyBase(host)
{
    private const string Amb19 = "AMB-19";

    /// <summary>The steps of AMB-19 and its variant whose confirm expects the offer: the first confirm.</summary>
    private const int FirstConfirmStep = 2;

    [Fact]
    public async Task APaymentThatNeededAQuestionOffersTheAliasAndNothingIsSavedWithoutTheTap()
    {
        var page = await OpenAsync(Host.WebAddress);
        var aliasesBefore = (await Host.AliasesAsync(U1)).Count;

        await PayS08Async(page);

        await Expect(page.GetByTestId("save-alias")).ToBeVisibleAsync();
        var registered = S08(await Host.ContactsAsync(U1)).RegisteredName!;
        await Expect(LastAgentMessage(page)).ToContainTextAsync(
            Text["Offer_save_alias", DevSet.AliasOffer(Amb19, FirstConfirmStep)!, registered].Value);

        // Step 3: the same recipient again asks again; the offer is gone, and nothing was saved.
        await SayAsync(page, DevSet.Say(Amb19, 1));
        await Expect(page.GetByTestId("question-option")).ToHaveCountAsync(2);
        await Expect(page.GetByTestId("save-alias")).ToHaveCountAsync(0);
        Assert.Equal(aliasesBefore, (await Host.AliasesAsync(U1)).Count);
    }

    [Fact]
    public async Task SaveOnTheOfferMakesTheNextRequestGoStraightToTheCard()
    {
        var page = await OpenAsync(Host.WebAddress);
        var aliasesBefore = (await Host.AliasesAsync(U1)).Count;
        var offered = DevSet.AliasOffer(Amb19, FirstConfirmStep)!;
        await PayS08Async(page);

        await page.GetByTestId("save-alias").ClickAsync();

        await Expect(LastAgentMessage(page)).ToContainTextAsync(Text["Reply_alias_saved", offered].Value);
        await Expect(page.GetByTestId("save-alias")).ToHaveCountAsync(0);
        var aliases = await Host.AliasesAsync(U1);
        Assert.Equal(aliasesBefore + 1, aliases.Count);
        var s08 = S08(await Host.ContactsAsync(U1));
        var saved = Assert.Single(aliases, a => a.Text == offered);
        Assert.Equal(s08.TargetAccountId, saved.TargetAccountId);
        Assert.Equal(1, await AuditCountAsync(AuditEvents.AliasSaved));

        // Step 3's sentence: no question, a card for the alias with S08's registered name under it.
        var questions = await page.Locator("[data-testid=chat-message][data-kind=question]").CountAsync();
        await SayAsync(page, DevSet.Say(Amb19, 1));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.Locator("[data-testid=chat-message][data-kind=question]")).ToHaveCountAsync(questions);
        await Expect(page.GetByTestId("card-display")).ToHaveTextAsync(offered);
        await Expect(page.GetByTestId("card-registered")).ToHaveTextAsync(s08.RegisteredName!);
    }

    [Fact]
    public async Task APaymentWithoutAQuestionOffersNoAlias()
    {
        var page = await OpenAsync(Host.WebAddress);

        await SayAsync(page, DevSet.Say("CLN-01"));
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("save-alias")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ANearSpellingOfASavedAliasShowsItAsAnOption()
    {
        var page = await OpenAsync(Host.WebAddress);
        var spoken = DevSet.AliasOffer("AMB-19-v1", FirstConfirmStep)!;

        await OpenDirectoryAsync(page);
        await AddInDirectoryAsync(page, spoken, "S08");
        await Expect(page.GetByTestId("directory-result")).ToHaveAttributeAsync("data-key", "Reply_alias_saved");
        await page.GetByTestId("phone-directory-back").ClickAsync();
        await Expect(page.GetByTestId("phone-input")).ToBeVisibleAsync();

        await SayAsync(page, DevSet.Say(Amb19, 1));

        var option = page.GetByTestId("question-option");
        await Expect(option).ToHaveCountAsync(1);
        await Expect(option).ToContainTextAsync(spoken);
        await option.ClickAsync();
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-display")).ToHaveTextAsync(spoken);
    }

    [Fact]
    public async Task ThePhoneDirectoryListsAddsAndDeletes()
    {
        var page = await OpenAsync(Host.WebAddress);
        var before = await Host.AliasesAsync(U1);
        var text = await SavedNameAsync("S03") + " x";

        await OpenDirectoryAsync(page);
        await Expect(page.GetByTestId("directory-alias")).ToHaveCountAsync(before.Count);

        await AddInDirectoryAsync(page, text, "S03");
        await Expect(page.GetByTestId("directory-result")).ToHaveAttributeAsync("data-key", "Reply_alias_saved");
        await Expect(page.GetByTestId("directory-alias")).ToHaveCountAsync(before.Count + 1);
        var added = Assert.Single(await Host.AliasesAsync(U1), a => a.Text == text);
        Assert.Equal(S03(await Host.ContactsAsync(U1)).TargetAccountId, added.TargetAccountId);
        var row = page.Locator($"[data-testid=directory-alias][data-alias-id='{added.AliasId}']");
        await Expect(row).ToContainTextAsync(text);

        await row.GetByTestId("directory-delete").ClickAsync();

        await Expect(page.GetByTestId("directory-result")).ToHaveAttributeAsync("data-key", "Reply_alias_deleted");
        await Expect(row).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("directory-alias")).ToHaveCountAsync(before.Count);
        Assert.DoesNotContain(await Host.AliasesAsync(U1), a => a.AliasId == added.AliasId);
    }

    [Fact]
    public async Task TheConsoleAliasesPanelListsAddsAndDeletes()
    {
        var page = await OpenAsync(Host.WebAddress);
        var before = await Host.AliasesAsync(U1);
        var text = await SavedNameAsync("S03") + " x";

        await page.GetByTestId("menu-aliases").ClickAsync();
        await Expect(page.GetByTestId("console-alias")).ToHaveCountAsync(before.Count);

        await page.GetByTestId("console-alias-text").FillAsync(text);
        await PickAsync(page, "console-alias-target", "console-alias-target-option", "S03");
        await page.GetByTestId("console-alias-save").ClickAsync();

        await Expect(page.GetByTestId("console-alias-result")).ToHaveAttributeAsync("data-result", "saved");
        await Expect(page.GetByTestId("console-alias")).ToHaveCountAsync(before.Count + 1);
        var added = Assert.Single(await Host.AliasesAsync(U1), a => a.Text == text);
        Assert.Equal(S03(await Host.ContactsAsync(U1)).TargetAccountId, added.TargetAccountId);

        await page.Locator($"[data-testid=console-alias-delete][data-alias-id='{added.AliasId}']").ClickAsync();

        await Expect(page.GetByTestId("console-alias-result")).ToHaveAttributeAsync("data-result", "deleted");
        await Expect(page.GetByTestId("console-alias")).ToHaveCountAsync(before.Count);
        Assert.DoesNotContain(await Host.AliasesAsync(U1), a => a.AliasId == added.AliasId);
    }

    [Fact]
    public async Task AnAliasSavedOnThePhoneShowsInTheConsoleDirectory()
    {
        // Two tabs on U1's console: two circuits, each with its own session. Only the first one's phone acts; both
        // aliases panels follow through the process-wide wallet-change notification (milestone 7 post-tag).
        var page = await OpenAsync(Host.WebAddress);
        var other = await OpenAsync(Host.WebAddress);
        var text = DevSet.AliasOffer(Amb19, FirstConfirmStep)!;
        var before = await Host.AliasesAsync(U1);

        foreach (var tab in new[] { page, other })
        {
            await tab.GetByTestId("menu-aliases").ClickAsync();
            await Expect(tab.GetByTestId("console-alias")).ToHaveCountAsync(before.Count);
        }

        await OpenDirectoryAsync(page);
        await AddInDirectoryAsync(page, text, "S08");
        await Expect(page.GetByTestId("directory-result")).ToHaveAttributeAsync("data-key", "Reply_alias_saved");

        var added = Assert.Single(await Host.AliasesAsync(U1), a => a.Text == text);
        await Expect(page.Locator($"[data-testid=directory-alias][data-alias-id='{added.AliasId}']")).ToContainTextAsync(text);
        foreach (var tab in new[] { page, other })
        {
            var row = tab.Locator($"[data-testid=console-alias][data-alias-id='{added.AliasId}']");
            await Expect(row).ToHaveCountAsync(1);
            await Expect(row).ToContainTextAsync(text);
            await Expect(tab.GetByTestId("console-alias")).ToHaveCountAsync(before.Count + 1);
        }

        await page.Locator($"[data-testid=directory-alias][data-alias-id='{added.AliasId}']").GetByTestId("directory-delete").ClickAsync();
        await Expect(page.GetByTestId("directory-result")).ToHaveAttributeAsync("data-key", "Reply_alias_deleted");

        foreach (var tab in new[] { page, other })
        {
            await Expect(tab.Locator($"[data-testid=console-alias][data-alias-id='{added.AliasId}']")).ToHaveCountAsync(0);
            await Expect(tab.GetByTestId("console-alias")).ToHaveCountAsync(before.Count);
        }

        Assert.DoesNotContain(await Host.AliasesAsync(U1), a => a.AliasId == added.AliasId);
    }

    [Fact]
    public async Task ADuplicateAliasIsRefusedInTheDirectory()
    {
        var page = await OpenAsync(Host.WebAddress);
        var before = await Host.AliasesAsync(U1);

        await OpenDirectoryAsync(page);
        await AddInDirectoryAsync(page, await SavedNameAsync("S04"), "S03");

        await Expect(page.GetByTestId("directory-result")).ToHaveAttributeAsync("data-key", "Refusal_alias_exists");
        await Expect(page.GetByTestId("directory-result")).ToHaveTextAsync(Text["Refusal_alias_exists"].Value);
        await Expect(page.GetByTestId("directory-alias")).ToHaveCountAsync(before.Count);
        Assert.Equal(before.Count, (await Host.AliasesAsync(U1)).Count);
    }

    [Fact]
    public async Task AnAliasForAClosedContactIsRefusedInTheDirectory()
    {
        var page = await OpenAsync(Host.WebAddress);
        var before = await Host.AliasesAsync(U1);
        var balanceBefore = await Host.BalanceAsync(U1);

        await OpenDirectoryAsync(page);
        await AddInDirectoryAsync(page, await SavedNameAsync("S20") + " x", "S20");

        await Expect(page.GetByTestId("directory-result")).ToHaveAttributeAsync("data-key", "Refusal_account_closed");
        await Expect(page.GetByTestId("directory-result")).ToHaveTextAsync(Text["Refusal_account_closed"].Value);
        await Expect(page.GetByTestId("directory-alias")).ToHaveCountAsync(before.Count);
        Assert.Equal(before, await Host.AliasesAsync(U1));
        Assert.Equal(balanceBefore, await Host.BalanceAsync(U1));
        Assert.Equal(0, await AuditCountAsync(AuditEvents.AliasSaved));
    }

    [Fact]
    public async Task ABareNameSharedBySeveralIsRefusedInTheDirectory()
    {
        var page = await OpenAsync(Host.WebAddress);
        var before = await Host.AliasesAsync(U1);

        // S04's first word alone: a word of S04, S05 and S06, so a question on several accounts (rule 4).
        await OpenDirectoryAsync(page);
        await AddInDirectoryAsync(page, (await SavedNameAsync("S04")).Split(' ')[0], "S04");

        await Expect(page.GetByTestId("directory-result")).ToHaveAttributeAsync("data-key", "Refusal_alias_shared");
        await Expect(page.GetByTestId("directory-result")).ToHaveTextAsync(Text["Refusal_alias_shared"].Value);
        await Expect(page.GetByTestId("directory-alias")).ToHaveCountAsync(before.Count);
        Assert.Equal(before, await Host.AliasesAsync(U1));
        Assert.Equal(0, await AuditCountAsync(AuditEvents.AliasSaved));
    }

    /// <summary>AMB-19's steps 0 to 2: the sentence, the tap on S08, the confirm; ends with the done outcome.</summary>
    private static async Task PayS08Async(IPage page)
    {
        await SayAsync(page, DevSet.Say(Amb19, 0));
        await page.Locator("[data-testid=question-option][data-value=S08]").ClickAsync();
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await page.GetByTestId("card-confirm").ClickAsync();
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(1);
    }

    private static async Task OpenDirectoryAsync(IPage page)
    {
        await page.GetByTestId("phone-directory").ClickAsync();
        await Expect(page.GetByTestId("phone-directory-screen")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("phone-input")).ToHaveCountAsync(0);
    }

    /// <summary>Fills the phone directory's form with <paramref name="text"/> for the contact <paramref name="code"/> and saves.</summary>
    private static async Task AddInDirectoryAsync(IPage page, string text, string code)
    {
        await page.GetByTestId("directory-text").FillAsync(text);
        await PickAsync(page, "directory-target", "directory-target-option", code);
        await page.GetByTestId("directory-save").ClickAsync();
    }

    /// <summary>Opens the select <paramref name="selectId"/> and clicks the item of contact <paramref name="code"/>.</summary>
    private static async Task PickAsync(IPage page, string selectId, string optionId, string code)
    {
        await page.GetByTestId(selectId).ClickAsync();
        var item = page.Locator($"[data-testid={optionId}][data-code={code}]");
        await item.ClickAsync();
        await Expect(item).ToHaveCountAsync(0);
    }

    /// <summary>The saved name of U1's contact <paramref name="code"/>, as the wallet holds it.</summary>
    private async Task<string> SavedNameAsync(string code)
        => (await Host.ContactsAsync(U1)).Contacts.Single(c => c.Code == code).SavedName;

    private static WalletContact S03(WalletContacts contacts) => contacts.Contacts.Single(c => c.Code == "S03");

    private static WalletContact S08(WalletContacts contacts) => contacts.Contacts.Single(c => c.Code == "S08");
}
