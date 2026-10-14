using System.Text.RegularExpressions;
using Wallet.MockApi.Data;

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
}
