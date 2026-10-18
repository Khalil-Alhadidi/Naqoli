using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data.Entities;
using Wallet.MockApi.Data.Seed;
using Wallet.MockApi.Services;

namespace Wallet.MockApi.Data;

/// <summary>
/// Builds the wallet's rows from the exported test-set seed (<see cref="SeedSet"/>) and inserts them. Every name,
/// number and code comes from the seed files; history is dated relative to "now" in Iraq time.
/// </summary>
public static class SeedData
{
    /// <summary>Iraq time: UTC+3, no daylight saving.</summary>
    public static readonly TimeSpan IraqOffset = TimeSpan.FromHours(3);

    private const string HistoryTimeFormat = "HH:mm";
    private const int OrderCodeDigits = 12;

    // PBKDF2 is slow on purpose, so each distinct seed PIN is hashed once per process and the hash is shared by
    // every Build: seeding and resets stop paying one hash per user each time.
    private static readonly ConcurrentDictionary<string, string> PinHashes = new(StringComparer.Ordinal);

    /// <summary>The calendar date in Iraq at <paramref name="now"/>.</summary>
    public static DateOnly TodayIn(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(IraqOffset).DateTime);

    /// <summary>
    /// The moment <paramref name="daysAgo"/> days before <paramref name="today"/> at <paramref name="time"/>
    /// (<c>HH:mm</c>), Iraq time.
    /// </summary>
    public static DateTimeOffset At(DateOnly today, int daysAgo, string time)
    {
        var clock = TimeOnly.ParseExact(time, HistoryTimeFormat, CultureInfo.InvariantCulture);
        return new DateTimeOffset(today.AddDays(-daysAgo).ToDateTime(clock), IraqOffset);
    }

    /// <summary>
    /// The purchase code of a seeded order: <c>9</c> and the digits of the history id, left-padded with zeros to 12
    /// (<c>H0086</c> gives <c>9000000000086</c>).
    /// </summary>
    public static string OrderCodeFor(string historyId)
    {
        ArgumentNullException.ThrowIfNull(historyId);
        return "9" + new string(historyId.Where(char.IsAsciiDigit).ToArray()).PadLeft(OrderCodeDigits, '0');
    }

    /// <summary>
    /// Adds the seed once and returns true; does nothing and returns false if any user already exists. Rows are inserted one at a time in a fixed
    /// order (users with their own account, the other accounts, billers, products, contacts, aliases, bill
    /// accounts, transactions, orders), so identity values are the same on every fresh database. Runs in a
    /// database transaction unless the caller already opened one.
    /// </summary>
    public static bool Apply(WalletDbContext db, SeedSet seed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(seed);

        if (db.Users.Any())
            return false;

        var graph = Build(seed, now);

        using var transaction = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;

        // Adding a user inserts its own account with it; the remaining accounts have no user.
        InsertInOrder(db, graph.Users);
        InsertInOrder(db, graph.Accounts.Where(a => a.User is null));
        InsertInOrder(db, graph.Billers);
        InsertInOrder(db, graph.Products);
        InsertInOrder(db, graph.Contacts);
        InsertInOrder(db, graph.Aliases);
        InsertInOrder(db, graph.BillAccounts);

        // Users, accounts, bill accounts and products now have their ids, so each history row can carry the
        // fingerprint of its own fields (its key is never accepted from a caller, so it is never compared).
        foreach (var t in graph.Transactions)
            t.RequestFingerprint = SeedFingerprint(t);
        InsertInOrder(db, graph.Transactions);
        InsertInOrder(db, graph.Orders);

        transaction?.Commit();
        return true;
    }

    /// <summary>
    /// Sets every user not consented, in one statement, and drops tracked entities so none holds the old value. Used by
    /// <see cref="SeedReset.ResetAsync"/> (inside its transaction) and by <c>migrate</c> after a fresh seed
    /// (Khalil, 18 Oct 2026).
    /// </summary>
    public static async Task ClearConsentAsync(WalletDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Users.ExecuteUpdateAsync(setters => setters.SetProperty(u => u.ConsentGiven, false), cancellationToken);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Maps the seed files to a fresh, untracked graph of entities, each list in insert order. History rows are
    /// dated from the Iraq date of <paramref name="now"/>.
    /// </summary>
    public static SeedGraph Build(SeedSet seed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var seedAccounts = seed.Accounts.ToDictionary(a => a.Number, StringComparer.Ordinal);

        // Users, each with its own account.
        List<User> users = [];
        List<Account> accounts = [];
        foreach (var u in seed.Users)
        {
            var user = new User
            {
                Code = u.Id,
                Phone = u.Number,
                DisplayName = u.Name,
                PinHash = PinHashes.GetOrAdd(u.Pin, PinHasher.Hash),
                ConsentGiven = u.ConsentGiven,
            };
            var account = NewAccount(seedAccounts[u.Number]);
            account.User = user;
            account.IsDefault = true;
            user.Accounts.Add(account);
            users.Add(user);
            accounts.Add(account);
        }

        // The other accounts (behind contacts and other numbers), in file order.
        var userPhones = users.Select(u => u.Phone).ToHashSet(StringComparer.Ordinal);
        accounts.AddRange(seed.Accounts.Where(a => !userPhones.Contains(a.Number)).Select(NewAccount));

        var accountsByPhone = accounts.ToDictionary(a => a.Phone, StringComparer.Ordinal);
        var usersByCode = users.ToDictionary(u => u.Code, StringComparer.Ordinal);

        var billers = seed.Billers
            .Select(b => new Biller { Code = b.Code, Name = b.NameAr, NameEn = b.NameEn })
            .ToList();
        var billersByCode = billers.ToDictionary(b => b.Code, StringComparer.Ordinal);

        var products = seed.Products
            .Select(p => new Product
            {
                Code = p.Id,
                Provider = p.Provider,
                ProviderNameEn = p.ProviderNameEn,
                ProviderNameAr = p.ProviderNameAr,
                AlsoCalled = p.AlsoCalled,
                Type = ProductTypeOf(p.Type),
                FaceValueIqd = p.FaceValue,
                PriceIqd = p.Price,
            })
            .ToList();
        var productsByCode = products.ToDictionary(p => p.Code, StringComparer.Ordinal);

        var contacts = seed.Contacts
            .Select(c => new Contact { Code = c.Id, Owner = usersByCode[c.User], SavedName = c.SavedName, Phone = c.Number })
            .ToList();

        // One alias, one account (D15): an alias points at the account behind its contact's number.
        List<Alias> aliases = [];
        foreach (var c in seed.Contacts)
        {
            if (c.Alias is { } text)
                aliases.Add(new Alias { Owner = usersByCode[c.User], Text = text, TargetAccount = accountsByPhone[c.Number] });
        }

        var billAccounts = seed.Bills
            .Select(b => new BillAccount
            {
                Code = b.Id,
                User = usersByCode[b.User],
                Biller = billersByCode[b.Biller],
                Label = b.Label,
                AccountNumber = b.SubscriberNumber,
                AmountDueIqd = b.AmountDue,
            })
            .ToList();
        var billAccountsByCode = billAccounts.ToDictionary(b => b.Code, StringComparer.Ordinal);

        // History in chronological order: oldest day first, then earliest time; ties keep file order (stable sort).
        var today = TodayIn(now);
        var history = seed.History
            .OrderByDescending(h => h.DaysAgo)
            .ThenBy(h => TimeOnly.ParseExact(h.Time, HistoryTimeFormat, CultureInfo.InvariantCulture))
            .ToList();

        List<Transaction> transactions = [];
        List<Order> orders = [];
        foreach (var row in history)
        {
            var payer = usersByCode[row.User];
            var at = At(today, row.DaysAgo, row.Time);

            // The fee is the file's, not WalletOptions', so a change to the configured fees never rewrites history.
            var transaction = new Transaction
            {
                IdempotencyKey = TransactionService.SeedKeyPrefix + row.Id,
                Reference = $"ZC-{transactions.Count + 1:D10}",
                User = payer,
                FromAccount = payer.Accounts.Single(a => a.IsDefault),
                Type = TransactionTypeOf(row.Type),
                Status = TransactionStatus.Done,
                AmountIqd = row.Amount,
                FeeIqd = row.Fee,
                TotalIqd = row.Amount + row.Fee,
                ToAccount = row.ToNumber is null ? null : accountsByPhone[row.ToNumber],
                BillAccount = row.Bill is null ? null : billAccountsByCode[row.Bill],
                Product = row.Product is null ? null : productsByCode[row.Product],
                CreatedAt = at,
                CompletedAt = at,
            };
            transactions.Add(transaction);

            if (transaction.Type == TransactionType.Purchase)
            {
                orders.Add(new Order
                {
                    User = payer,
                    Product = transaction.Product,
                    Transaction = transaction,
                    Code = OrderCodeFor(row.Id),
                    CreatedAt = at,
                });
            }
        }

        return new SeedGraph(users, accounts, billers, products, contacts, aliases, billAccounts, transactions, orders);
    }

    /// <summary>
    /// The fingerprint of a seeded row: its payer, type and amount, the recipient's phone for a transfer, and its bill
    /// account or product id. Needs the ids, so it runs once the rows they belong to are inserted.
    /// </summary>
    private static string SeedFingerprint(Transaction t) => RequestFingerprint.Of(new PaymentRequest(
        t.User!.Id,
        TransactionNames.Of(t.Type),
        t.AmountIqd,
        t.ToAccount?.Phone,
        null,
        t.BillAccount?.Id,
        t.Product?.Id,
        null));

    private static Account NewAccount(SeedAccount account) => new()
    {
        Code = account.Id,
        Phone = account.Number,
        RegisteredName = account.RegisteredName,
        Status = account.Status switch
        {
            "active" => AccountStatus.Active,
            "closed" => AccountStatus.Closed,
            _ => throw new SeedException($"{SeedFiles.Accounts}: account {account.Number} has status {account.Status}"),
        },
        BalanceIqd = account.Balance,
    };

    private static ProductType ProductTypeOf(string type) => type switch
    {
        "credit" => ProductType.Credit,
        "internet" => ProductType.Internet,
        _ => throw new SeedException($"{SeedFiles.Store}: unknown product type {type}"),
    };

    private static TransactionType TransactionTypeOf(string type)
        => TransactionNames.TryParse(type, out var parsed)
            ? parsed
            : throw new SeedException($"{SeedFiles.History}: unknown transaction type {type}");

    private static void InsertInOrder<T>(DbContext db, IEnumerable<T> rows) where T : class
    {
        foreach (var row in rows)
        {
            db.Add(row);
            db.SaveChanges();
        }
    }
}

/// <summary>The entities built from the seed files, each list in insert order.</summary>
/// <param name="Users">The users; each holds its own account in <see cref="User.Accounts"/>.</param>
/// <param name="Accounts">Every account: the users' own accounts first, then the others in file order.</param>
/// <param name="Billers">The billers, in file order.</param>
/// <param name="Products">The store catalogue, in file order.</param>
/// <param name="Contacts">The contacts, in file order.</param>
/// <param name="Aliases">One alias per contact that has one, in contact order.</param>
/// <param name="BillAccounts">The bill accounts, in file order.</param>
/// <param name="Transactions">The history, oldest first.</param>
/// <param name="Orders">One order per purchase, in transaction order.</param>
public sealed record SeedGraph(
    List<User> Users,
    List<Account> Accounts,
    List<Biller> Billers,
    List<Product> Products,
    List<Contact> Contacts,
    List<Alias> Aliases,
    List<BillAccount> BillAccounts,
    List<Transaction> Transactions,
    List<Order> Orders);
