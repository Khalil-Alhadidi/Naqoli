namespace Wallet.MockApi.Data;

/// <summary>
/// The Arabic seed values that tests and checks refer to by name, so that no Arabic text sits in test code.
/// They are part of the frozen seed: <see cref="SeedData"/> builds its rows from these constants, and any change
/// alters <see cref="SeedData.ComputeHash"/>. Every name is invented.
/// </summary>
public static class SeedStrings
{
    /// <summary>Registered name of user 1, the demo user (Ahmed).</summary>
    public const string User1RegisteredName = "أحمد كريم جاسم";

    /// <summary>Registered name of user 2 (Sara).</summary>
    public const string User2RegisteredName = "سارة عبد الله حسن";

    /// <summary>Registered name of user 4 (Mahmoud), whom user 1 saved as Hammoudi.</summary>
    public const string User4RegisteredName = "محمود كريم جاسم";

    /// <summary>User 1's saved name for user 4 (Hammoudi).</summary>
    public const string SavedNameHammoudi = "حمودي";

    /// <summary>User 1's saved name for users 5 and 6: the planted duplicate (Ali).</summary>
    public const string SavedNameAli = "علي";

    /// <summary>User 1's saved name for user 12 (Abu Ali); it contains <see cref="SavedNameAli"/>.</summary>
    public const string SavedNameAbuAli = "ابو علي";

    /// <summary>User 1's alias for user 4's account ("my brother").</summary>
    public const string AliasBrother = "اخوي";

    /// <summary>User 1's alias for user 9's account ("home").</summary>
    public const string AliasHome = "البيت";

    /// <summary>The label of user 1's home bill accounts, and of user 2's ("home"); the same text as <see cref="AliasHome"/>.</summary>
    public const string BillLabelHome = "البيت";

    /// <summary>The electricity biller's name.</summary>
    public const string BillerElectricity = "الكهرباء";

    /// <summary>Product 6: Asiacell internet, 10 GB. User 1's latest seeded purchase.</summary>
    public const string ProductAsiacellInternet10Gb = "انترنت آسياسيل ١٠ جيجا";
}
