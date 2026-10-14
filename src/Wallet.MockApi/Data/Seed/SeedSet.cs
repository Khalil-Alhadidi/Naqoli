using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wallet.MockApi.Data.Seed;

/// <summary>
/// The exported test-set seed, read from the eight files in <c>data/seed</c> (see <c>data/README.md</c>) and
/// checked for internal consistency. The files are the source; nothing here invents a row.
/// </summary>
public sealed class SeedSet
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,

        // A missing constructor parameter without a default, or a null in a non-nullable one, is a JsonException
        // (wrapped into a SeedException naming the file) rather than a null that breaks validation later.
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private SeedSet(
        string directory,
        IReadOnlyList<SeedUser> users,
        IReadOnlyList<SeedAccount> accounts,
        IReadOnlyList<SeedContact> contacts,
        IReadOnlyList<SeedBiller> billers,
        IReadOnlyList<SeedBill> bills,
        IReadOnlyList<SeedProduct> products,
        SeedSettings settings,
        IReadOnlyList<SeedHistoryRow> history)
    {
        Directory = directory;
        Users = users;
        Accounts = accounts;
        Contacts = contacts;
        Billers = billers;
        Bills = bills;
        Products = products;
        Settings = settings;
        History = history;
    }

    /// <summary>The directory the files were read from.</summary>
    public string Directory { get; }

    public IReadOnlyList<SeedUser> Users { get; }

    public IReadOnlyList<SeedAccount> Accounts { get; }

    public IReadOnlyList<SeedContact> Contacts { get; }

    public IReadOnlyList<SeedBiller> Billers { get; }

    public IReadOnlyList<SeedBill> Bills { get; }

    /// <summary>The store catalogue (<c>store.json</c>).</summary>
    public IReadOnlyList<SeedProduct> Products { get; }

    public SeedSettings Settings { get; }

    public IReadOnlyList<SeedHistoryRow> History { get; }

    /// <summary>Reads the eight seed files from <paramref name="directory"/> and validates them.</summary>
    /// <exception cref="SeedException">
    /// A file is missing, is not valid JSON, has a property the models do not know, or fails a consistency check.
    /// The message starts with the file name.
    /// </exception>
    public static SeedSet Load(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var seed = new SeedSet(
            directory,
            Read<List<SeedUser>>(directory, SeedFiles.Users),
            Read<List<SeedAccount>>(directory, SeedFiles.Accounts),
            Read<List<SeedContact>>(directory, SeedFiles.Contacts),
            Read<List<SeedBiller>>(directory, SeedFiles.Billers),
            Read<List<SeedBill>>(directory, SeedFiles.Bills),
            Read<List<SeedProduct>>(directory, SeedFiles.Store),
            Read<SeedSettings>(directory, SeedFiles.Settings),
            Read<List<SeedHistoryRow>>(directory, SeedFiles.History));

        seed.Validate();
        return seed;
    }

    private static T Read<T>(string directory, string file)
        where T : class
    {
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            throw new SeedException($"{file}: not found in {directory}");

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json)
                ?? throw new SeedException($"{file}: the file holds null");
        }
        catch (JsonException ex)
        {
            throw new SeedException($"{file}: {ex.Message}", ex);
        }
    }

    private void Validate()
    {
        // 1. Unique keys.
        Unique(SeedFiles.Users, "user id", Users.Select(u => u.Id));
        Unique(SeedFiles.Accounts, "account number", Accounts.Select(a => a.Number));
        Unique(SeedFiles.Contacts, "contact id", Contacts.Select(c => c.Id));
        Unique(SeedFiles.Billers, "biller code", Billers.Select(b => b.Code));
        Unique(SeedFiles.Bills, "bill id", Bills.Select(b => b.Id));
        Unique(SeedFiles.Store, "product id", Products.Select(p => p.Id));
        Unique(SeedFiles.History, "history id", History.Select(h => h.Id));

        var accounts = Accounts.ToDictionary(a => a.Number, StringComparer.Ordinal);
        var userIds = Users.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var billerCodes = Billers.Select(b => b.Code).ToHashSet(StringComparer.Ordinal);
        var billIds = Bills.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var productIds = Products.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        // 5. Allowed values (before the cross-file checks, which rely on Kind).
        foreach (var a in Accounts)
        {
            if (a.Status is not ("active" or "closed"))
                throw Fail(SeedFiles.Accounts, $"account {a.Number} has status {a.Status}, expected active or closed");
            if (a.Kind is not ("user" or "contact" or "other"))
                throw Fail(SeedFiles.Accounts, $"account {a.Number} has kind {a.Kind}, expected user, contact or other");
        }

        foreach (var p in Products)
        {
            if (p.Type is not ("credit" or "internet"))
                throw Fail(SeedFiles.Store, $"product {p.Id} has type {p.Type}, expected credit or internet");
        }

        foreach (var h in History)
        {
            if (h.Type is not ("transfer" or "bill" or "purchase"))
                throw Fail(SeedFiles.History, $"row {h.Id} has type {h.Type}, expected transfer, bill or purchase");
            if (h.Status != "done")
                throw Fail(SeedFiles.History, $"row {h.Id} has status {h.Status}, expected done");
            if (!TimeOnly.TryParseExact(h.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw Fail(SeedFiles.History, $"row {h.Id} has time {h.Time}, expected HH:mm");
        }

        // 2. Every user is a user account with the same name and balance.
        foreach (var u in Users)
        {
            if (!accounts.TryGetValue(u.Number, out var account) || account.Kind != "user")
                throw Fail(SeedFiles.Users, $"user {u.Id} has number {u.Number}, which is not an account of kind user");
            if (!string.Equals(account.RegisteredName, u.Name, StringComparison.Ordinal))
                throw Fail(SeedFiles.Users, $"user {u.Id} has a different name from account {u.Number}");
            if (account.Balance != u.Balance)
                throw Fail(SeedFiles.Users, $"user {u.Id} has balance {u.Balance}, account {u.Number} has {account.Balance}");
        }

        // 3. Contacts, bills and history belong to users; contact numbers are accounts.
        foreach (var c in Contacts)
        {
            if (!userIds.Contains(c.User))
                throw Fail(SeedFiles.Contacts, $"contact {c.Id} belongs to {c.User}, which is not a user");
            if (!accounts.ContainsKey(c.Number))
                throw Fail(SeedFiles.Contacts, $"contact {c.Id} points at number {c.Number}, which is not an account");
        }

        foreach (var b in Bills)
        {
            if (!userIds.Contains(b.User))
                throw Fail(SeedFiles.Bills, $"bill {b.Id} belongs to {b.User}, which is not a user");

            // 4. Bills name a known biller.
            if (!billerCodes.Contains(b.Biller))
                throw Fail(SeedFiles.Bills, $"bill {b.Id} names biller {b.Biller}, which is not a biller");
        }

        // 4. History rows point at seed rows and carry the reference their type needs.
        foreach (var h in History)
        {
            if (!userIds.Contains(h.User))
                throw Fail(SeedFiles.History, $"row {h.Id} belongs to {h.User}, which is not a user");
            if (h.ToNumber is not null && !accounts.ContainsKey(h.ToNumber))
                throw Fail(SeedFiles.History, $"row {h.Id} points at number {h.ToNumber}, which is not an account");
            if (h.Bill is not null && !billIds.Contains(h.Bill))
                throw Fail(SeedFiles.History, $"row {h.Id} points at bill {h.Bill}, which is not a bill");
            if (h.Product is not null && !productIds.Contains(h.Product))
                throw Fail(SeedFiles.History, $"row {h.Id} points at product {h.Product}, which is not a product");

            var missing = h.Type switch
            {
                "transfer" when h.ToNumber is null => "to_number",
                "bill" when h.Bill is null => "bill",
                "purchase" when h.Product is null => "product",
                _ => null,
            };
            if (missing is not null)
                throw Fail(SeedFiles.History, $"row {h.Id} is a {h.Type} without {missing}");
        }

        // 6. One alias, one account (D15); no repeated (user, saved name, number).
        foreach (var group in Contacts.Where(c => c.Alias is not null).GroupBy(c => (c.User, c.Alias)))
        {
            if (group.Skip(1).Any())
                throw Fail(SeedFiles.Contacts, $"user {group.Key.User} has the alias on more than one contact: {string.Join(", ", group.Select(c => c.Id))}");
        }

        foreach (var group in Contacts.GroupBy(c => (c.User, c.SavedName, c.Number)))
        {
            if (group.Skip(1).Any())
                throw Fail(SeedFiles.Contacts, $"user {group.Key.User} has the same saved name and number twice: {string.Join(", ", group.Select(c => c.Id))}");
        }

        // 7. Nothing negative.
        foreach (var u in Users.Where(u => u.Balance < 0))
            throw Fail(SeedFiles.Users, $"user {u.Id} has a negative balance");
        foreach (var a in Accounts.Where(a => a.Balance < 0))
            throw Fail(SeedFiles.Accounts, $"account {a.Number} has a negative balance");
        foreach (var b in Bills.Where(b => b.AmountDue < 0))
            throw Fail(SeedFiles.Bills, $"bill {b.Id} has a negative amount due");
        foreach (var p in Products.Where(p => p.FaceValue < 0 || p.Price < 0))
            throw Fail(SeedFiles.Store, $"product {p.Id} has a negative face value or price");
        foreach (var h in History.Where(h => h.Amount < 0 || h.Fee < 0 || h.DaysAgo < 0))
            throw Fail(SeedFiles.History, $"row {h.Id} has a negative amount, fee or days_ago");
    }

    private static void Unique(string file, string what, IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!seen.Add(key))
                throw Fail(file, $"{what} {key} appears more than once");
        }
    }

    private static SeedException Fail(string file, string problem) => new($"{file}: {problem}");
}

/// <summary>A seed file is missing, malformed or inconsistent. The message starts with the file name.</summary>
public sealed class SeedException : Exception
{
    public SeedException(string message)
        : base(message)
    {
    }

    public SeedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
