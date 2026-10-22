extern alias AgentWeb;

using System.Globalization;
using AgentWeb::Agent.Web.Services;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// The phone's Help screen (22 Oct 2026): opened from the header's question mark in place of the chat, with no compose
/// row, five sections and the development-set example lines, and Back to the chat as it was. Every text is read from the
/// resource file.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class HelpJourneys(JourneyHost host) : JourneyBase(host)
{
    [Fact]
    public async Task HelpShowsTheFiveSectionsAndReturnsToTheChat()
    {
        var page = await OpenAsync(Host.WebAddress);
        var messages = await page.GetByTestId("chat-message").CountAsync();

        await page.GetByTestId("phone-help").ClickAsync();

        var help = page.GetByTestId("phone-help-screen");
        await Expect(help).ToBeVisibleAsync();
        await Expect(page.GetByTestId("phone-input")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("phone-send")).ToHaveCountAsync(0);
        await Expect(help.GetByTestId("help-title")).ToHaveTextAsync(Text["Help_Title"].Value);

        var sections = help.GetByTestId("help-section");
        await Expect(sections).ToHaveCountAsync(5);
        string[] expected = ["transfer", "bill", "purchase", "balance", "not_done"];
        for (var i = 0; i < expected.Length; i++)
            await Expect(sections.Nth(i)).ToHaveAttributeAsync("data-section", expected[i]);

        var transfer = help.Locator("[data-testid=help-section][data-section=transfer]").GetByTestId("help-example");
        await Expect(transfer).ToHaveCountAsync(3);
        for (var i = 0; i < 3; i++)
        {
            var line = PhoneFormat.Digits(Text["Help_Example_Transfer_" + (i + 1).ToString(CultureInfo.InvariantCulture)].Value);
            await Expect(transfer.Nth(i)).ToBeVisibleAsync();
            await Expect(transfer.Nth(i)).ToHaveTextAsync(line);
        }

        await Expect(help.GetByTestId("help-not-done")).ToHaveCountAsync(5);

        await page.GetByTestId("phone-help-back").ClickAsync();

        await Expect(help).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("phone-input")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("chat-message")).ToHaveCountAsync(messages);
    }
}
