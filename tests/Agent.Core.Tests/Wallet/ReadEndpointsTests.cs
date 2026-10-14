using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

[Collection(WalletApiCollection.Name)]
public class ReadEndpointsTests(WalletApiFixture fixture)
{
    private const string HammoudiPhone = "07700001004";
    private const string Ali = "علي";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserByPhoneReturnsRegisteredName()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var user = await fixture.Client.GetFromJsonAsync<UserSummary>($"/users/by-phone/{HammoudiPhone}", Token);

        Assert.NotNull(user);
        Assert.Equal(4, user.Id);
        Assert.Equal(HammoudiPhone, user.Phone);
        Assert.Equal("محمود كريم جاسم", user.RegisteredName);

        var demo = await fixture.Client.GetFromJsonAsync<UserSummary>($"/users/{SeedData.DemoUserId}", Token);
        Assert.Equal(new UserSummary(1, "07700001001", "أحمد كريم جاسم"), demo);
    }

    [Theory]
    [InlineData("/users/999")]
    [InlineData("/users/by-phone/07700009999")]
    [InlineData("/users/999/contacts")]
    [InlineData("/users/999/bills")]
    [InlineData("/users/999/balance")]
    [InlineData("/users/999/history")]
    [InlineData("/users/999/orders")]
    public async Task UnknownUserIs404ProblemDetails(string path)
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var response = await fixture.Client.GetAsync(path, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("User not found", problem.Title);
    }

    [Fact]
    public async Task ContactsOfDemoUserIncludeTwoAliAndLastSentToHammoudi()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var result = await fixture.Client.GetFromJsonAsync<ContactsResponse>($"/users/{SeedData.DemoUserId}/contacts", Token);

        Assert.NotNull(result);
        Assert.Equal(10, result.Contacts.Count);
        Assert.Equal(2, result.Contacts.Count(c => c.SavedName == Ali));

        var hammoudi = Assert.Single(result.Contacts, c => c.Phone == HammoudiPhone);
        Assert.Equal("حمودي", hammoudi.SavedName);
        Assert.Equal("محمود كريم جاسم", hammoudi.RegisteredName);
        Assert.Equal(4, hammoudi.TargetAccountId);
        Assert.Equal(25000, hammoudi.LastSentAmountIqd);
        Assert.Equal(SeedData.Anchor.AddDays(-7), hammoudi.LastSentAt);

        var husain = Assert.Single(result.Contacts, c => c.Phone == "07700001008");
        Assert.Null(husain.LastSentAt);
        Assert.Null(husain.LastSentAmountIqd);

        Assert.Equal(3, result.Aliases.Count);
        var brother = Assert.Single(result.Aliases, a => a.Text == "اخوي");
        Assert.Equal(4, brother.TargetAccountId);
        Assert.Equal(HammoudiPhone, brother.Phone);
        Assert.Equal("محمود كريم جاسم", brother.RegisteredName);
    }

    [Fact]
    public async Task ContactsQueryFiltersBySubstring()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var result = await fixture.Client.GetFromJsonAsync<ContactsResponse>(
            $"/users/{SeedData.DemoUserId}/contacts?q={Uri.EscapeDataString(Ali)}", Token);

        Assert.NotNull(result);
        Assert.Equal(["07700001005", "07700001006", "07700001012"], result.Contacts.Select(c => c.Phone));
        Assert.Equal(2, result.Contacts.Count(c => c.SavedName == Ali));
        Assert.Single(result.Contacts, c => c.SavedName == "ابو علي");
        Assert.Empty(result.Aliases);

        var byAlias = await fixture.Client.GetFromJsonAsync<ContactsResponse>(
            $"/users/{SeedData.DemoUserId}/contacts?q={Uri.EscapeDataString("بيت")}", Token);
        Assert.NotNull(byAlias);
        Assert.Empty(byAlias.Contacts);
        Assert.Equal("البيت", Assert.Single(byAlias.Aliases).Text);
    }

    [Fact]
    public async Task BillsOfDemoUserListFourWithOneNothingDue()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var bills = await fixture.Client.GetFromJsonAsync<List<BillItem>>($"/users/{SeedData.DemoUserId}/bills", Token);

        Assert.NotNull(bills);
        Assert.Equal(4, bills.Count);
        Assert.Single(bills, b => b.AmountDueIqd == 0);
        Assert.Equal(2, bills.Count(b => b.BillerCode == "electricity"));

        var home = Assert.Single(bills, b => b.AccountNumber == "10000000001");
        Assert.Equal(45000, home.AmountDueIqd);
        Assert.Equal(new DateOnly(2026, 10, 25), home.DueDate);
        Assert.Equal("الكهرباء", home.BillerName);
        Assert.Equal("البيت", home.Label);
    }

    [Fact]
    public async Task BalanceOfDemoUserIs300000()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var balance = await fixture.Client.GetFromJsonAsync<BalanceResponse>($"/users/{SeedData.DemoUserId}/balance", Token);

        Assert.NotNull(balance);
        Assert.Equal(1, balance.AccountId);
        Assert.Equal(300000, balance.BalanceIqd);
        Assert.NotEqual(default, balance.AsOf);
    }

    [Fact]
    public async Task HistoryToHammoudiHasFourTransfersNewestFirst()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var history = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>(
            $"/users/{SeedData.DemoUserId}/history?to={HammoudiPhone}", Token);

        Assert.NotNull(history);
        Assert.Equal([4, 3, 2, 1], history.Select(h => h.TransactionId));
        Assert.Equal([25000L, 50000L, 50000L, 50000L], history.Select(h => h.AmountIqd));
        Assert.All(history, h =>
        {
            Assert.Equal("Transfer", h.Type);
            Assert.Equal("Done", h.Status);
            Assert.Equal(HammoudiPhone, h.CounterpartyPhone);
            Assert.Equal("محمود كريم جاسم", h.CounterpartyName);
            Assert.Equal(1000, h.FeeIqd);
            Assert.Equal(h.AmountIqd + h.FeeIqd, h.TotalIqd);
        });
        Assert.Equal("ZC-0000000004", history[0].Reference);
        Assert.Equal(SeedData.Anchor.AddDays(-7), history[0].CreatedAt);
    }

    [Fact]
    public async Task HistorySinceLimitsResults()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var basePath = $"/users/{SeedData.DemoUserId}/history";
        var since = Uri.EscapeDataString(SeedData.Anchor.AddDays(-10).ToString("O"));

        var all = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>(basePath, Token);
        var recent = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>($"{basePath}?since={since}", Token);
        var lastTwo = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>($"{basePath}?since={since}&take=2", Token);
        var clamped = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>($"{basePath}?take=500", Token);

        Assert.NotNull(all);
        Assert.Equal(18, all.Count);
        Assert.Equal(all.OrderByDescending(h => h.CreatedAt).Select(h => h.TransactionId), all.Select(h => h.TransactionId));
        Assert.Null(Assert.Single(all, h => h.TransactionId == 12).CounterpartyPhone);
        Assert.Equal(6, Assert.Single(all, h => h.TransactionId == 18).ProductId);

        Assert.Equal([10, 4, 9], recent!.Select(h => h.TransactionId));
        Assert.Equal([10, 4], lastTwo!.Select(h => h.TransactionId));
        Assert.Equal(18, clamped!.Count);
    }

    [Fact]
    public async Task ProductsFilterByProviderTypeAndValue()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var zainCredit = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?provider=Zain&type=Credit", Token);
        var fiveThousand = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?value=5000", Token);
        var all = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products", Token);
        var lowerCase = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?provider=zain&type=internet", Token);

        Assert.NotNull(zainCredit);
        Assert.Equal([1, 2], zainCredit.Select(p => p.ProductId));
        Assert.All(zainCredit, p => Assert.Equal("Credit", p.Type));
        Assert.Equal([1, 4, 7], fiveThousand!.Select(p => p.ProductId));
        Assert.Equal(8, all!.Count);
        Assert.Equal(3, Assert.Single(lowerCase!).ProductId);
    }

    [Fact]
    public async Task OrdersOfDemoUserListTwoWithCodes()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var orders = await fixture.Client.GetFromJsonAsync<List<OrderItem>>($"/users/{SeedData.DemoUserId}/orders", Token);

        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);
        Assert.Equal([18, 17], orders.Select(o => o.TransactionId));
        Assert.Equal(["1234567890002", "1234567890001"], orders.Select(o => o.Code));
        Assert.Equal([6, 1], orders.Select(o => o.ProductId));
        Assert.Equal("انترنت آسياسيل ١٠ جيجا", orders[0].ProductName);
    }
}
