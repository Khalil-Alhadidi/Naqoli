using System.Text.RegularExpressions;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;

namespace Agent.Core.Tests.Wallet;

public class SeedDataTests
{
    [Fact]
    public void AtLeastTwoUsers()
        => Assert.True(SeedData.Users().Count >= 2);

    [Fact]
    public void EveryUserHasExactlyOneDefaultAccount()
    {
        foreach (var user in SeedData.Users())
            Assert.Equal(1, user.Accounts.Count(a => a.IsDefault));
    }

    [Fact]
    public void BalancesAreNeverNegative()
    {
        foreach (var account in SeedData.Users().SelectMany(u => u.Accounts))
            Assert.True(account.BalanceIqd >= 0, $"{account.UserId} has {account.BalanceIqd}");
    }

    [Fact]
    public void PhonesAreSyntheticAndUnique()
    {
        var phones = SeedData.Users().Select(u => u.Phone).ToList();
        Assert.All(phones, p => Assert.Matches(new Regex(@"^0770000\d{4}$"), p));
        Assert.Equal(phones.Count, phones.Distinct().Count());
    }

    [Fact]
    public void DemoUserHasTenContactsIncludingTwoNamedAli()
    {
        var demo = DemoUser(SeedData.Users());

        Assert.Equal(10, demo.Contacts.Count);
        Assert.Equal(2, demo.Contacts.Count(c => c.SavedName == SeedStrings.SavedNameAli));
    }

    [Fact]
    public void EveryContactPhoneBelongsToASeededUser()
    {
        var users = SeedData.Users();
        var phones = users.Select(u => u.Phone).ToHashSet();

        var contacts = users.SelectMany(u => u.Contacts).ToList();
        Assert.NotEmpty(contacts);
        Assert.All(contacts, c => Assert.Contains(c.Phone, phones));
    }

    [Fact]
    public void EveryAliasTargetsExactlyOneDefaultAccount()
    {
        var users = SeedData.Users();
        var defaultAccounts = users.SelectMany(u => u.Accounts).Where(a => a.IsDefault).ToList();

        var aliases = users.SelectMany(u => u.Aliases).ToList();
        Assert.NotEmpty(aliases);
        foreach (var alias in aliases)
        {
            Assert.NotNull(alias.TargetAccount);
            Assert.Single(defaultAccounts, a => ReferenceEquals(a, alias.TargetAccount));
        }

        foreach (var user in users)
            Assert.Equal(user.Aliases.Count, user.Aliases.Select(a => a.Text).Distinct().Count());
    }

    [Fact]
    public void DemoUserHasTwoElectricityAccountsAndOneWithNothingDue()
    {
        var demo = DemoUser(SeedData.Users());

        Assert.Equal(2, demo.BillAccounts.Count(b => b.Biller?.Code == "electricity"));
        Assert.Single(demo.BillAccounts, b => b.AmountDueIqd == 0);
    }

    [Fact]
    public void CatalogueHasEightProductsAcrossThreeProviders()
    {
        var products = SeedData.Products();

        Assert.Equal(8, products.Count);
        Assert.Equal(3, products.Select(p => p.Provider).Distinct().Count());
    }

    [Fact]
    public void DemoUserHistorySpansThreeMonths()
    {
        var history = SeedData.Transactions().Where(t => t.User?.Phone == DemoPhone).ToList();

        Assert.NotEmpty(history);
        Assert.True(history.Min(t => t.CreatedAt) <= SeedData.Anchor.AddDays(-85));
        Assert.True(history.Max(t => t.CreatedAt) >= SeedData.Anchor.AddDays(-5));
    }

    [Fact]
    public void NoPinIsStoredInClear()
    {
        foreach (var user in SeedData.Users())
        {
            Assert.NotEqual("1234", user.PinHash);
            Assert.StartsWith("pbkdf2$", user.PinHash);
        }
    }

    private const string DemoPhone = "07700001001";

    private static User DemoUser(IReadOnlyList<User> users) => users.Single(u => u.Phone == DemoPhone);
}
