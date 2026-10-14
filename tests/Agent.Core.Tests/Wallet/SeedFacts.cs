using Wallet.MockApi.Data.Seed;

namespace Agent.Core.Tests.Wallet;

/// <summary>
/// The seed figures the wallet tests rely on. Ids follow the seed's insert order (see <c>SeedData.Apply</c>);
/// Arabic text and PINs are read from the seed files, never written here.
/// </summary>
internal static class SeedFacts
{
    /// <summary>U1, the everyday user the agent acts for.</summary>
    public const int DemoUserId = 1;

    /// <summary>U1's own account.</summary>
    public const int DemoAccountId = 1;

    public const string DemoPhone = "07805550001";

    public const long DemoBalance = 750_000;

    /// <summary>Contact S01 of U1. Its account id is looked up by phone, never assumed.</summary>
    public const string HammoudiPhone = "07805550101";

    /// <summary>U3, with a balance above the PIN threshold.</summary>
    public const int TraderUserId = 3;

    /// <summary>U2, with a tight balance.</summary>
    public const int TightUserId = 2;

    public static SeedSet Seed => SeedSetTests.Seed;

    public static string PinOf(string userCode) => Seed.Users.Single(u => u.Id == userCode).Pin;

    public static SeedContact Contact(string code) => Seed.Contacts.Single(c => c.Id == code);

    public static SeedAccount Account(string phone) => Seed.Accounts.Single(a => a.Number == phone);
}
