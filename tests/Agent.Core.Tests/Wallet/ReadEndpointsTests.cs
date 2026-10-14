using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;

namespace Agent.Core.Tests.Wallet;

/// <summary>
/// The read endpoints against the seed. Every test starts from a reset (<see cref="InitializeAsync"/>), so the
/// assertions never depend on which test class ran before.
/// </summary>
[Collection(WalletApiCollection.Name)]
public sealed class ReadEndpointsTests(WalletApiFixture fixture) : IAsyncLifetime
{
    private const string HammoudiPhone = SeedFacts.HammoudiPhone;
    private const int DemoUserId = SeedFacts.DemoUserId;
    private const string TraderPhone = "07905550003";

    /// <summary>U5, who has not given consent yet.</summary>
    private const int FirstUseUserId = 5;

    /// <summary>X04: a closed account with no registered name.</summary>
    private const string ClosedOtherPhone = "07805550193";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (fixture.DatabaseAvailable)
            await fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task UserSummaryCarriesCodeAndConsent()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var demo = await fixture.Client.GetFromJsonAsync<UserSummary>($"/users/{DemoUserId}", Token);
        Assert.Equal(
            new UserSummary(DemoUserId, "U1", SeedFacts.DemoPhone, SeedFacts.Account(SeedFacts.DemoPhone).RegisteredName!, true),
            demo);

        var trader = await fixture.Client.GetFromJsonAsync<UserSummary>($"/users/{SeedFacts.TraderUserId}", Token);
        Assert.NotNull(trader);
        Assert.Equal("U3", trader.Code);
        Assert.Equal(TraderPhone, trader.Phone);
        Assert.Equal(SeedFacts.Account(TraderPhone).RegisteredName, trader.RegisteredName);

        // U5 has not given consent yet.
        var firstUse = await fixture.Client.GetFromJsonAsync<UserSummary>($"/users/{FirstUseUserId}", Token);
        Assert.NotNull(firstUse);
        Assert.Equal("U5", firstUse.Code);
        Assert.False(firstUse.ConsentGiven);
    }

    [Fact]
    public async Task AccountByPhoneFindsContactsOthersAndClosed()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var hammoudi = await fixture.Client.GetFromJsonAsync<AccountSummary>($"/accounts/by-phone/{HammoudiPhone}", Token);
        Assert.Equal(
            new AccountSummary(await AccountIdAsync(HammoudiPhone), HammoudiPhone, SeedFacts.Account(HammoudiPhone).RegisteredName, "active"),
            hammoudi);

        // X04: a closed account with no registered name, saved by nobody.
        var closed = await fixture.Client.GetFromJsonAsync<AccountSummary>($"/accounts/by-phone/{ClosedOtherPhone}", Token);
        Assert.NotNull(closed);
        Assert.Equal(ClosedOtherPhone, closed.Phone);
        Assert.Null(closed.RegisteredName);
        Assert.Equal("closed", closed.Status);

        var unknown = await fixture.Client.GetAsync("/accounts/by-phone/07805559999", Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var problem = await unknown.ReadProblemAsync(Token);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Account not found", problem.Title);
        Assert.Equal("unknown_account", problem.Code());

        // The old user-by-phone route is gone.
        var oldRoute = await fixture.Client.GetAsync($"/users/by-phone/{SeedFacts.DemoPhone}", Token);
        Assert.Equal(HttpStatusCode.NotFound, oldRoute.StatusCode);
    }

    [Fact]
    public async Task ConsentCanBeGivenOnce()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var path = $"/users/{FirstUseUserId}";

        var first = await fixture.Client.PostAsync($"{path}/consent", content: null, Token);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.True((await fixture.Client.GetFromJsonAsync<UserSummary>(path, Token))!.ConsentGiven);

        // Giving consent again changes nothing and is not an error.
        var second = await fixture.Client.PostAsync($"{path}/consent", content: null, Token);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.True((await fixture.Client.GetFromJsonAsync<UserSummary>(path, Token))!.ConsentGiven);

        var unknown = await fixture.Client.PostAsync("/users/99/consent", content: null, Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("unknown_user", (await unknown.ReadProblemAsync(Token)).Code());

        // A reset restores the seed, where U5 has not given consent.
        await fixture.ResetAsync();
        Assert.False((await fixture.Client.GetFromJsonAsync<UserSummary>(path, Token))!.ConsentGiven);
    }

    [Fact]
    public async Task ContactsCarryCodesAndAccountStatus()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var result = await fixture.Client.GetFromJsonAsync<ContactsResponse>($"/users/{DemoUserId}/contacts", Token);

        Assert.NotNull(result);
        var closed = Assert.Single(result.Contacts, c => c.Code == "S20");
        Assert.Equal(SeedFacts.Contact("S20").Number, closed.Phone);
        Assert.Equal("closed", closed.AccountStatus);

        var hammoudi = Assert.Single(result.Contacts, c => c.Phone == HammoudiPhone);
        Assert.Equal("S01", hammoudi.Code);
        Assert.Equal("active", hammoudi.AccountStatus);

        Assert.Equal(4, result.Aliases.Count);
        Assert.All(result.Aliases, a =>
        {
            Assert.False(string.IsNullOrEmpty(a.Phone));
            Assert.Equal(SeedFacts.Account(a.Phone).RegisteredName, a.RegisteredName);
            Assert.Equal(SeedFacts.Account(a.Phone).Status, a.AccountStatus);
        });
    }

    [Fact]
    public async Task BillsCarryCodesAndEnglishNames()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var bills = await fixture.Client.GetFromJsonAsync<List<BillItem>>($"/users/{DemoUserId}/bills", Token);

        Assert.NotNull(bills);
        Assert.Equal(["BS1", "BS2", "BS3", "BS4"], bills.Select(b => b.Code));
        Assert.Equal(
            ["Electricity", "Electricity", "Water", "Internet (Earthlink subscription)"],
            bills.Select(b => b.BillerNameEn));
        Assert.Null(Assert.Single(bills, b => b.Code == "BS3").Label);
    }

    [Fact]
    public async Task ProductsCarryCodesAndProviderNames()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var seedNetForty = SeedFacts.Seed.Products.Single(p => p.Id == "ZN-NET-40");

        var all = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products", Token);
        var upperCaseZain = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?provider=ZAIN", Token);

        Assert.NotNull(all);
        var netForty = Assert.Single(all, p => p.Code == "ZN-NET-40");
        Assert.Equal("zain", netForty.Provider);
        Assert.Equal("Zain", netForty.ProviderNameEn);
        Assert.Equal(seedNetForty.ProviderNameAr, netForty.ProviderNameAr);
        Assert.NotNull(netForty.AlsoCalled);
        Assert.Equal(seedNetForty.AlsoCalled, netForty.AlsoCalled);
        Assert.Equal("internet", netForty.Type);

        Assert.Null(Assert.Single(all, p => p.Code == "KR-CR-5").AlsoCalled);

        Assert.Equal(6, upperCaseZain!.Count);
        Assert.All(upperCaseZain, p => Assert.Equal("zain", p.Provider));
    }

    [Fact]
    public async Task OrdersCarryProductCodes()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var orders = await fixture.Client.GetFromJsonAsync<List<OrderItem>>($"/users/{DemoUserId}/orders", Token);

        Assert.NotNull(orders);
        Assert.Equal(["ZN-NET-25", "AC-CR-5", "ZN-NET-25", "ZN-CR-10"], orders.Select(o => o.ProductCode));
    }

    [Fact]
    public async Task HistoryCarriesCounterpartyAccountId()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var account = await fixture.Client.GetFromJsonAsync<AccountSummary>($"/accounts/by-phone/{HammoudiPhone}", Token);

        var history = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>(
            $"/users/{DemoUserId}/history?to={HammoudiPhone}", Token);

        Assert.NotNull(history);
        Assert.Equal(account!.AccountId, Assert.Single(history).CounterpartyAccountId);

        // Bills and purchases have no counterparty account.
        var all = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>($"/users/{DemoUserId}/history", Token);
        Assert.All(all!.Where(h => h.Type != "transfer"), h => Assert.Null(h.CounterpartyAccountId));
    }

    [Theory]
    [InlineData("/users/999")]
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
        Assert.Equal("unknown_user", problem.Code());
    }

    [Fact]
    public async Task ContactsOfDemoUserIncludeTwoHusainAndLastSentToHammoudi()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var hammoudiAccountId = await AccountIdAsync(HammoudiPhone);
        var husain = SeedFacts.Contact("S08").SavedName;

        var result = await fixture.Client.GetFromJsonAsync<ContactsResponse>($"/users/{DemoUserId}/contacts", Token);

        Assert.NotNull(result);
        Assert.Equal(20, result.Contacts.Count);
        Assert.Equal(2, result.Contacts.Count(c => c.SavedName == husain));

        var hammoudi = Assert.Single(result.Contacts, c => c.Phone == HammoudiPhone);
        Assert.Equal(SeedFacts.Contact("S01").SavedName, hammoudi.SavedName);
        Assert.Equal(SeedFacts.Account(HammoudiPhone).RegisteredName, hammoudi.RegisteredName);
        Assert.Equal(hammoudiAccountId, hammoudi.TargetAccountId);
        Assert.Equal(50000, hammoudi.LastSentAmountIqd);
        Assert.Equal(SeedData.At(Today(), 9, "19:34"), hammoudi.LastSentAt);

        // S18: a contact U1 never sent to.
        var neverSent = Assert.Single(result.Contacts, c => c.Phone == SeedFacts.Contact("S18").Number);
        Assert.Null(neverSent.LastSentAt);
        Assert.Null(neverSent.LastSentAmountIqd);

        Assert.Equal(4, result.Aliases.Count);
        var brother = Assert.Single(result.Aliases, a => a.Text == SeedFacts.Contact("S01").Alias);
        Assert.Equal(hammoudiAccountId, brother.TargetAccountId);
        Assert.Equal(HammoudiPhone, brother.Phone);
        Assert.Equal(SeedFacts.Account(HammoudiPhone).RegisteredName, brother.RegisteredName);
    }

    [Fact]
    public async Task ContactsQueryFiltersBySubstring()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        // The first word of S04's saved name (Ali): S04 to S07 contain it.
        var ali = SeedFacts.Contact("S04").SavedName.Split(' ')[0];
        var result = await fixture.Client.GetFromJsonAsync<ContactsResponse>(
            $"/users/{DemoUserId}/contacts?q={Uri.EscapeDataString(ali)}", Token);

        Assert.NotNull(result);
        Assert.Equal(
            new[] { "S04", "S05", "S06", "S07" }.Select(code => SeedFacts.Contact(code).Number),
            result.Contacts.Select(c => c.Phone));
        Assert.Empty(result.Aliases);

        // S14's alias without its two-letter article: a substring that only that alias contains.
        var motherAlias = SeedFacts.Contact("S14").Alias!;
        var byAlias = await fixture.Client.GetFromJsonAsync<ContactsResponse>(
            $"/users/{DemoUserId}/contacts?q={Uri.EscapeDataString(motherAlias[2..])}", Token);
        Assert.NotNull(byAlias);
        Assert.Empty(byAlias.Contacts);
        Assert.Equal(motherAlias, Assert.Single(byAlias.Aliases).Text);
    }

    [Fact]
    public async Task BillsOfDemoUserListFourAndTightUserHasOneNothingDue()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var seedHome = SeedFacts.Seed.Bills.Single(b => b.Id == "BS1");
        var electricity = SeedFacts.Seed.Billers.Single(b => b.Code == "electricity");

        var bills = await fixture.Client.GetFromJsonAsync<List<BillItem>>($"/users/{DemoUserId}/bills", Token);

        Assert.NotNull(bills);
        Assert.Equal(4, bills.Count);
        Assert.DoesNotContain(bills, b => b.AmountDueIqd == 0);
        Assert.Equal(2, bills.Count(b => b.BillerCode == "electricity"));

        var home = Assert.Single(bills, b => b.BillAccountId == 1);
        Assert.Equal(seedHome.SubscriberNumber, home.AccountNumber);
        Assert.Equal(43750, home.AmountDueIqd);
        Assert.Equal(electricity.NameAr, home.BillerName);
        Assert.Equal(seedHome.Label, home.Label);

        // U2's BZ1 (bill account 5) has nothing due.
        var tight = await fixture.Client.GetFromJsonAsync<List<BillItem>>($"/users/{SeedFacts.TightUserId}/bills", Token);
        var nothingDue = Assert.Single(tight!, b => b.AmountDueIqd == 0);
        Assert.Equal(5, nothingDue.BillAccountId);
    }

    [Fact]
    public async Task BalanceOfDemoUserIs750000()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var balance = await fixture.Client.GetFromJsonAsync<BalanceResponse>($"/users/{DemoUserId}/balance", Token);

        Assert.NotNull(balance);
        Assert.Equal(SeedFacts.DemoAccountId, balance.AccountId);
        Assert.Equal(SeedFacts.DemoBalance, balance.BalanceIqd);
        Assert.NotEqual(default, balance.AsOf);
    }

    [Fact]
    public async Task HistoryToHammoudiHasOneTransfer()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var history = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>(
            $"/users/{DemoUserId}/history?to={HammoudiPhone}", Token);

        Assert.NotNull(history);
        var row = Assert.Single(history);
        Assert.Equal(73, row.TransactionId);
        Assert.Equal(50000L, row.AmountIqd);
        Assert.Equal("transfer", row.Type);
        Assert.Equal("done", row.Status);
        Assert.Equal(HammoudiPhone, row.CounterpartyPhone);
        Assert.Equal(SeedFacts.Account(HammoudiPhone).RegisteredName, row.CounterpartyName);
        Assert.Equal(1000, row.FeeIqd);
        Assert.Equal(row.AmountIqd + row.FeeIqd, row.TotalIqd);
        Assert.Equal("ZC-0000000073", row.Reference);
        Assert.Equal(SeedData.At(Today(), 9, "19:34"), row.CreatedAt);
    }

    [Fact]
    public async Task HistorySinceLimitsResults()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var basePath = $"/users/{DemoUserId}/history";
        var since = Uri.EscapeDataString(SeedData.At(Today(), 10, "00:00").ToString("O"));

        var all = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>(basePath, Token);
        var recent = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>($"{basePath}?since={since}", Token);
        var lastTwo = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>($"{basePath}?since={since}&take=2", Token);
        var clamped = await fixture.Client.GetFromJsonAsync<List<HistoryItem>>($"{basePath}?take=500", Token);

        // U1 has 50 rows, as many as the default page.
        Assert.NotNull(all);
        Assert.Equal(50, all.Count);
        Assert.Equal(all.OrderByDescending(h => h.CreatedAt).Select(h => h.TransactionId), all.Select(h => h.TransactionId));
        Assert.Null(Assert.Single(all, h => h.TransactionId == 56).CounterpartyPhone);
        Assert.Equal(5, Assert.Single(all, h => h.TransactionId == 68).ProductId);

        // H0022, H0014, H0017, H0026, H0023, H0027, H0001: the rows of the last ten days, newest first.
        Assert.Equal([84, 82, 79, 78, 76, 74, 73], recent!.Select(h => h.TransactionId));
        Assert.Equal([84, 82], lastTwo!.Select(h => h.TransactionId));
        Assert.Equal(50, clamped!.Count);
    }

    [Fact]
    public async Task ProductsFilterByProviderTypeAndValue()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var zainCredit = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?provider=Zain&type=Credit", Token);
        var fiveThousand = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?value=5000", Token);
        var all = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products", Token);
        var zain = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?provider=zain", Token);
        var internetForty = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?type=internet&value=40000", Token);

        Assert.NotNull(zainCredit);
        Assert.Equal([1, 2, 3], zainCredit.Select(p => p.ProductId));
        Assert.All(zainCredit, p => Assert.Equal("credit", p.Type));
        Assert.Equal([1, 7, 12], fiveThousand!.Select(p => p.ProductId));
        Assert.Equal(15, all!.Count);
        Assert.Equal(6, zain!.Count);
        Assert.All(zain, p => Assert.Equal("zain", p.Provider));

        // ZN-NET-40.
        var zainNetForty = Assert.Single(internetForty!);
        Assert.Equal(6, zainNetForty.ProductId);
        Assert.Equal("internet", zainNetForty.Type);
        Assert.Equal(40000, zainNetForty.PriceIqd);

        // Any case is accepted; anything but a type name matches nothing.
        var upperCase = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?type=CREDIT", Token);
        var number = await fixture.Client.GetFromJsonAsync<List<ProductItem>>("/store/products?type=0", Token);
        Assert.Equal(8, upperCase!.Count);
        Assert.Empty(number!);
    }

    [Fact]
    public async Task OrdersOfDemoUserListFourWithCodes()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);

        var orders = await fixture.Client.GetFromJsonAsync<List<OrderItem>>($"/users/{DemoUserId}/orders", Token);

        // H0078 (ZN-NET-25), H0081 (AC-CR-5), H0080 (ZN-NET-25), H0079 (ZN-CR-10), newest first.
        Assert.NotNull(orders);
        Assert.Equal(4, orders.Count);
        Assert.Equal([68, 54, 5, 3], orders.Select(o => o.TransactionId));
        Assert.Equal(["9000000000078", "9000000000081", "9000000000080", "9000000000079"], orders.Select(o => o.Code));
        Assert.Equal([5, 7, 5, 2], orders.Select(o => o.ProductId));
        Assert.Equal(SeedData.At(Today(), 12, "08:46"), orders[0].CreatedAt);
    }

    // Seeded dates follow the Iraq date of the clock the API uses.
    private DateOnly Today()
    {
        using var scope = fixture.CreateScope();
        return SeedData.TodayIn(scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow());
    }

    private async Task<int> AccountIdAsync(string phone)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.Accounts.AsNoTracking().Where(a => a.Phone == phone).Select(a => a.Id).SingleAsync(Token);
    }
}
