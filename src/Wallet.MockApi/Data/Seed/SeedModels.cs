namespace Wallet.MockApi.Data.Seed;

/// <summary>The names of the eight exported seed files (see <c>data/README.md</c>) and where the app reads them.</summary>
public static class SeedFiles
{
    public const string Users = "users.json";
    public const string Accounts = "accounts.json";
    public const string Contacts = "contacts.json";
    public const string Billers = "billers.json";
    public const string Bills = "bills.json";
    public const string Store = "store.json";
    public const string Settings = "settings.json";
    public const string History = "history.json";

    /// <summary>Every seed file, in the order the seed hash reads them.</summary>
    public static readonly string[] All = [Users, Accounts, Contacts, Billers, Bills, Store, Settings, History];

    /// <summary>The copy of <c>data/seed</c> in the build output.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "seed");
}

/// <summary>A wallet user the agent can act for (<c>users.json</c>).</summary>
public sealed record SeedUser(
    string Id,
    string Name,
    string Number,
    long Balance,
    bool ConsentGiven,
    string Pin,
    string? RoleInTestSet);

/// <summary>A wallet account (<c>accounts.json</c>). <see cref="Id"/> is set for kind <c>other</c> only.</summary>
public sealed record SeedAccount(
    string Number,
    string? RegisteredName,
    string Status,
    long Balance,
    string Kind,
    string? Id);

/// <summary>A saved contact of one user (<c>contacts.json</c>).</summary>
public sealed record SeedContact(
    string User,
    string Id,
    string SavedName,
    string? Alias,
    string Number,
    string? Why);

/// <summary>A biller (<c>billers.json</c>).</summary>
public sealed record SeedBiller(string Code, string NameEn, string NameAr);

/// <summary>A bill account of one user (<c>bills.json</c>).</summary>
public sealed record SeedBill(
    string User,
    string Id,
    string Biller,
    string? Label,
    string SubscriberNumber,
    long AmountDue,
    string? Note);

/// <summary>A store product (<c>store.json</c>).</summary>
public sealed record SeedProduct(
    string Id,
    string Provider,
    string ProviderNameEn,
    string ProviderNameAr,
    string? AlsoCalled,
    string Type,
    long FaceValue,
    long Price);

/// <summary>A past transaction (<c>history.json</c>), dated <see cref="DaysAgo"/> days before the seed's "now".</summary>
public sealed record SeedHistoryRow(
    string Id,
    string User,
    int DaysAgo,
    string Time,
    string Type,
    long Amount,
    long Fee,
    string Status,
    string? ToNumber,
    string? Contact,
    string? Bill,
    string? Biller,
    string? Product);

/// <summary>The test set's limits, fees and lists (<c>settings.json</c>).</summary>
public sealed record SeedSettings(
    string Currency,
    SeedFees Fees,
    long MinAmount,
    SeedPin Pin,
    SeedCaps Caps,
    string[] CheckOrder,
    int CardTimerSeconds,
    int MaxRequestsPerMessage,
    int MaxProductsInOneQuestion,
    int RepeatWarningWindowMinutes,
    int HistoryDays,
    SeedFault[] Faults,
    SeedLearningLog LearningLog);

/// <summary>Fees per request type, in whole dinars.</summary>
public sealed record SeedFees(long Transfer, long Bill, long Purchase);

/// <summary>The PIN threshold and the attempts allowed.</summary>
public sealed record SeedPin(long RequiredAbove, int Attempts);

/// <summary>The per-transaction and per-day caps, and what they count.</summary>
public sealed record SeedCaps(long PerTransaction, long PerDay, string Counts);

/// <summary>A fault the test set can inject.</summary>
public sealed record SeedFault(string Code, string Description);

/// <summary>Which outcomes go to the learning log and which do not.</summary>
public sealed record SeedLearningLog(string[] Logged, string[] NotLogged);
