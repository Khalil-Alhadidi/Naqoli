using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Data;

/// <summary>The synthetic users and accounts the mock wallet starts with.</summary>
public static class SeedData
{
    /// <summary>Returns a fresh, untracked list on every call.</summary>
    public static IReadOnlyList<User> Users() =>
    [
        NewUser("07700001001", "Ahmed Test", 300000),
        NewUser("07700001002", "Sara Test", 1250000),
        NewUser("07700001003", "Omar Test", 0),
    ];

    /// <summary>Adds the seed users once. Does nothing if any user already exists.</summary>
    public static void Apply(WalletDbContext db)
    {
        if (db.Users.Any())
            return;

        db.Users.AddRange(Users());
        db.SaveChanges();
    }

    private static User NewUser(string phone, string displayName, long balanceIqd) => new()
    {
        Phone = phone,
        DisplayName = displayName,
        Accounts = [new Account { IsDefault = true, BalanceIqd = balanceIqd }],
    };
}
