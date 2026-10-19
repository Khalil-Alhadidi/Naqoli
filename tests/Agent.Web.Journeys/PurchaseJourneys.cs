extern alias AgentWeb;

using Agent.Core.Data;
using Agent.Core.Pipeline;
using Agent.Core.Text;
using Agent.Core.Wallet;
using AgentWeb::Agent.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Agent.Web.Journeys;

/// <summary>
/// Milestone 9b, store purchases on screen: a named product straight to the card and its code with a copy button, the
/// code kept by its last four digits only, a bare value listed across the operators, the operator asked first, a Latin
/// operator spelling, an unknown product refused and logged, My orders, and a cancelled purchase. Every sentence is a
/// development-set case typed by id; every name, code and figure is read from the wallet, the dev set or the resource file.
/// </summary>
[Collection(JourneyCollection.Name)]
public sealed class PurchaseJourneys(JourneyHost host) : JourneyBase(host)
{
    private static ILocator Questions(IPage page) => page.Locator("[data-testid=chat-message][data-kind=question]");

    private static async Task<IReadOnlyList<string?>> OptionValuesAsync(IPage page)
    {
        var values = new List<string?>();
        foreach (var option in await page.GetByTestId("question-option").AllAsync())
            values.Add(await option.GetAttributeAsync("data-value"));
        return values;
    }

    private async Task<WalletProduct> ProductAsync(string code) => (await Host.ProductsAsync()).Single(p => p.Code == code);

    private async Task ExpectPurchaseCardAsync(IPage page, WalletProduct product)
    {
        await Expect(page.GetByTestId("card")).ToHaveAttributeAsync("data-state", "live");
        await Expect(page.GetByTestId("card-display")).ToHaveTextAsync(PhoneFormat.Digits(product.ProviderNameAr));
        // Security review R4 of milestone 9b: the type word beside the operator, from the catalogue product's type.
        await Expect(page.GetByTestId("card-product-type")).ToHaveTextAsync(Text["Product_type_" + product.Type].Value);
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(product.PriceIqd, Text));
    }

    /// <summary>CLN-13 for U1: Zain internet of 25,000, straight to the card, confirmed. Returns the new order.</summary>
    private async Task<WalletOrder> BuyCln13Async(IPage page)
    {
        var before = (await Host.OrdersAsync(U1)).Count;
        await SayAsync(page, DevSet.Say("CLN-13"));
        await ExpectPurchaseCardAsync(page, await ProductAsync("ZN-NET-25"));
        await Expect(Questions(page)).ToHaveCountAsync(0);

        await page.GetByTestId("card-confirm").ClickAsync();
        await Expect(Outcomes(page, "done")).ToHaveCountAsync(2);

        var orders = await Host.OrdersAsync(U1);
        Assert.Equal(before + 1, orders.Count);
        Assert.Equal("ZN-NET-25", orders[0].ProductCode);
        return orders[0];
    }

    /// <summary>The agent's pending records, flattened to their text columns.</summary>
    private async Task<IReadOnlyList<string>> PendingRowsAsync()
    {
        await using var scope = Host.WebServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var records = await db.PendingTransactions.AsNoTracking().ToListAsync(Token);
        return [.. records.Select(r => string.Join('|', r.Type, r.State, r.DisplayName, r.ProductCode, r.Reference, r.ResultCode, r.PurchaseCodeLast4))];
    }

    [Fact]
    public async Task ANamedProductGoesStraightToTheCardAndTheCodeShowsWithACopyButton()
    {
        var page = await OpenAsync(Host.WebAddress);

        var order = await BuyCln13Async(page);

        Assert.Contains(order.Code, DigitsOf(await Outcomes(page, "done").Nth(1).InnerTextAsync()), StringComparison.Ordinal);
        var copy = page.GetByTestId("purchase-code-copy");
        await Expect(copy).ToBeVisibleAsync();
        await Expect(copy).ToHaveAttributeAsync("data-copied", "false");

        await copy.ClickAsync();

        await Expect(copy).ToHaveAttributeAsync("data-copied", "true");
        Assert.Equal(order.Code, await ClipboardAsync(page));
    }

    [Fact]
    public async Task TheCodeIsKeptOnlyByItsLastFourDigitsInTheLogs()
    {
        var page = await OpenAsync(Host.WebAddress);

        var order = await BuyCln13Async(page);

        string[] whole = [order.Code, ArabicDigits.ToArabicIndic(order.Code)];
        var last4 = order.Code[^4..];

        // The pending record keeps the last four (PendingStore.PurchaseCodeDigitsKept); the audit and learning rows
        // never hold the whole code, nor does any record.
        var pending = await PendingRowsAsync();
        Assert.Contains(pending, row => row.EndsWith("|" + last4, StringComparison.Ordinal));
        string[] rows = [.. await AgentRowsAsync(), .. pending];
        Assert.All(rows, row => Assert.All(whole, code => Assert.DoesNotContain(code, row, StringComparison.Ordinal)));

        // The live trace never shows the code, in either digit form.
        var entries = await TextsAsync(page, "trace-entry");
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.DoesNotContain(order.Code, DigitsOf(entry), StringComparison.Ordinal);
            Assert.All(whole, code => Assert.DoesNotContain(code, entry, StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task ABareValueListsTheMatchingProductsAcrossOperators()
    {
        var page = await OpenAsync(Host.WebAddress);
        var kr5 = await ProductAsync("KR-CR-5");

        await SayAsync(page, DevSet.Say("AMB-23"));

        await Expect(Questions(page)).ToHaveCountAsync(1);
        await Expect(Questions(page)).ToHaveTextAsync(Text["Question_" + QuestionKinds.WhichProduct].Value);
        Assert.Equal(
            new[] { "AC-CR-5", "KR-CR-5", "ZN-CR-5" },
            (await OptionValuesAsync(page)).Order(StringComparer.Ordinal));
        var typeWord = Text["Product_type_credit"].Value;
        Assert.All(await TextsAsync(page, "question-option"), text => Assert.Contains(typeWord, text, StringComparison.Ordinal));
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);

        await page.Locator("[data-testid=question-option][data-value=KR-CR-5]").ClickAsync();

        await ExpectPurchaseCardAsync(page, kr5);
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(5_000, Text));

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(2);
        Assert.Equal(745_000, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task NothingNamedAsksTheOperatorFirstThenTheProducts()
    {
        var page = await OpenAsync(Host.WebAddress);
        var catalogue = await Host.ProductsAsync();
        var korek = catalogue.Where(p => p.Provider == "korek").Select(p => p.Code).Order(StringComparer.Ordinal).ToArray();

        await SayAsync(page, DevSet.Say("AMB-24"));

        await Expect(Questions(page)).ToHaveCountAsync(1);
        await Expect(Questions(page)).ToHaveTextAsync(Text["Question_" + QuestionKinds.WhichOperator].Value);
        Assert.Equal(new[] { "zain", "asiacell", "korek" }, await OptionValuesAsync(page));

        await page.Locator("[data-testid=question-option][data-value=korek]").ClickAsync();

        await Expect(Questions(page)).ToHaveCountAsync(2);
        await Expect(Questions(page).Last).ToHaveTextAsync(Text["Question_" + QuestionKinds.WhichProduct].Value);
        Assert.Equal(korek, (await OptionValuesAsync(page)).Order(StringComparer.Ordinal));

        await page.Locator("[data-testid=question-option][data-value=KR-NET-30]").ClickAsync();

        await ExpectPurchaseCardAsync(page, await ProductAsync("KR-NET-30"));
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(30_000, Text));
    }

    [Fact]
    public async Task ALatinOperatorSpellingGoesStraightToTheCard()
    {
        var page = await OpenAsync(Host.UserWebAddress(U2));
        var ac5 = await ProductAsync("AC-CR-5");

        await SayAsync(page, DevSet.Say("MAL-09"));

        // "asia" is configured under the provider code asiacell (Agent:ProviderSpellings, MAL-09).
        await ExpectPurchaseCardAsync(page, ac5);
        await Expect(page.GetByTestId("card-amount")).ToHaveTextAsync(PhoneFormat.Amount(5_000, Text));
        await Expect(Questions(page)).ToHaveCountAsync(0);

        await page.GetByTestId("card-confirm").ClickAsync();

        await Expect(Outcomes(page, "done")).ToHaveCountAsync(2);
        Assert.Equal(13_500, await Host.BalanceAsync(U2));
    }

    [Fact]
    public async Task AnUnknownProductIsRefusedAndLoggedWithTheProviderCode()
    {
        var page = await OpenAsync(Host.WebAddress);

        await SayAsync(page, DevSet.Say("NOGO-12"));

        await Expect(LastAgentMessage(page)).ToHaveTextAsync(Text["Refusal_" + ReasonCodes.ProductNotFound].Value);
        await Expect(page.GetByTestId("card")).ToHaveCountAsync(0);
        Assert.Contains((ReasonCodes.ProductNotFound, (string?)"korek credit 25000"), await LearningRowsAsync());
        Assert.Equal(U1Balance, await Host.BalanceAsync(U1));
    }

    [Fact]
    public async Task MyOrdersListsThePurchaseCodesNewestFirstAndCopies()
    {
        var page = await OpenAsync(Host.WebAddress);
        var product = await ProductAsync("ZN-NET-25");
        var order = await BuyCln13Async(page);
        var orders = await Host.OrdersAsync(U1);

        await page.GetByTestId("phone-orders").ClickAsync();

        await Expect(page.GetByTestId("phone-orders-screen")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("order-row")).ToHaveCountAsync(orders.Count);
        var first = page.GetByTestId("order-row").First;
        await Expect(first).ToHaveAttributeAsync("data-order-id", order.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(order.Code, DigitsOf(await first.GetByTestId("order-code").InnerTextAsync()));
        var productLine = await first.GetByTestId("order-product").InnerTextAsync();
        Assert.Contains(product.ProviderNameAr, productLine, StringComparison.Ordinal);
        Assert.Contains(Text["Product_type_internet"].Value, productLine, StringComparison.Ordinal);
        await Expect(page.GetByTestId("phone-input")).ToHaveCountAsync(0);

        var copy = first.GetByTestId("order-copy");
        await copy.ClickAsync();

        await Expect(copy).ToHaveAttributeAsync("data-copied", "true");
        Assert.Equal(order.Code, await ClipboardAsync(page));

        await page.GetByTestId("phone-orders-back").ClickAsync();

        await Expect(page.GetByTestId("phone-orders-screen")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("phone-input")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ACancelledPurchaseIssuesNoCode()
    {
        var page = await OpenAsync(Host.UserWebAddress(U3));
        var before = (await Host.OrdersAsync(U3)).Count;
        var balance = await Host.BalanceAsync(U3);

        await SayAsync(page, DevSet.Say("CLN-29"));

        await ExpectPurchaseCardAsync(page, await ProductAsync("AC-NET-25"));

        await page.GetByTestId("card-cancel").ClickAsync();

        await Expect(Outcomes(page, "cancelled")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("purchase-code-copy")).ToHaveCountAsync(0);
        Assert.Equal(before, (await Host.OrdersAsync(U3)).Count);
        Assert.Equal(balance, await Host.BalanceAsync(U3));
        Assert.Equal(12_500_000, await Host.BalanceAsync(U3));
    }
}
