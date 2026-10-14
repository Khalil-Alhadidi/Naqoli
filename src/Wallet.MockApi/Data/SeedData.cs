using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Data.Entities;
using Wallet.MockApi.Services;

namespace Wallet.MockApi.Data;

/// <summary>
/// The frozen synthetic seed of the mock wallet: users, accounts, contacts, aliases, billers, bill accounts,
/// the store catalogue and three months of history. Every name, number and code is invented.
/// Any change alters <see cref="ComputeHash"/> and fails the seed-hash test on purpose
/// (see <c>data/seed/README.md</c>).
/// </summary>
public static class SeedData
{
    /// <summary>The demo user the agent acts for (Ahmed).</summary>
    public const int DemoUserId = 1;

    /// <summary>The fixed "now" of the seed. History dates are counted back from it.</summary>
    public static readonly DateTimeOffset Anchor = new(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(3));

    private const string PinPlaceholder = "***";

    private static readonly JsonSerializerOptions HashJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Returns a fresh, untracked graph on every call: users with accounts, contacts, aliases and bill accounts.</summary>
    public static IReadOnlyList<User> Users() => Build(HashSeedPin).Users;

    /// <summary>Returns a fresh, untracked list on every call.</summary>
    public static IReadOnlyList<Biller> Billers() => Build(HashSeedPin).Billers;

    /// <summary>Returns a fresh, untracked list on every call.</summary>
    public static IReadOnlyList<Product> Products() => Build(HashSeedPin).Products;

    /// <summary>The seeded history, in insert order, linked to its users, accounts, bill accounts and products.</summary>
    public static IReadOnlyList<Transaction> Transactions() => Build(HashSeedPin).Transactions;

    /// <summary>The seeded store orders, linked to their transactions.</summary>
    public static IReadOnlyList<Order> Orders() => Build(HashSeedPin).Orders;

    /// <summary>
    /// Adds the seed once. Does nothing if any user already exists. Rows are inserted one at a time,
    /// users (with accounts) first, then billers, products, contacts, aliases, bill accounts, transactions
    /// and orders, so identity values are the same on every fresh database. Runs in a database
    /// transaction unless the caller already opened one.
    /// </summary>
    public static void Apply(WalletDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (db.Users.Any())
            return;

        var seed = Build(HashSeedPin);

        // Take the rows each user owns off the graph so that adding a user inserts only the user and its accounts.
        var contacts = seed.Users.SelectMany(u => u.Contacts).ToList();
        var aliases = seed.Users.SelectMany(u => u.Aliases).ToList();
        var billAccounts = seed.Users.SelectMany(u => u.BillAccounts).ToList();
        foreach (var user in seed.Users)
        {
            user.Contacts.Clear();
            user.Aliases.Clear();
            user.BillAccounts.Clear();
        }

        using var transaction = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;

        InsertInOrder(db, seed.Users);
        InsertInOrder(db, seed.Billers);
        InsertInOrder(db, seed.Products);
        InsertInOrder(db, contacts);
        InsertInOrder(db, aliases);
        InsertInOrder(db, billAccounts);
        InsertInOrder(db, seed.Transactions);
        InsertInOrder(db, seed.Orders);

        transaction?.Commit();
    }

    /// <summary>
    /// SHA-256 (lower-case hex) of a canonical JSON form of every seed row, in seed order.
    /// PIN hashes are salted, so the hash carries a placeholder instead.
    /// </summary>
    public static string ComputeHash()
    {
        var seed = Build(() => PinPlaceholder);

        var canonical = new
        {
            users = seed.Users.Select(u => new
            {
                phone = u.Phone,
                displayName = u.DisplayName,
                pin = PinPlaceholder,
                pinFailedAttempts = u.PinFailedAttempts,
                pinLocked = u.PinLocked,
                accounts = u.Accounts.Select(a => new { isDefault = a.IsDefault, balanceIqd = a.BalanceIqd }),
                contacts = u.Contacts.Select(c => new { savedName = c.SavedName, phone = c.Phone }),
                aliases = u.Aliases.Select(a => new { text = a.Text, targetPhone = a.TargetAccount!.User!.Phone }),
                billAccounts = u.BillAccounts.Select(b => new
                {
                    biller = b.Biller!.Code,
                    label = b.Label,
                    accountNumber = b.AccountNumber,
                    amountDueIqd = b.AmountDueIqd,
                    dueDate = b.DueDate,
                }),
            }),
            billers = seed.Billers.Select(b => new { code = b.Code, name = b.Name }),
            products = seed.Products.Select(p => new
            {
                provider = p.Provider,
                type = p.Type.ToString(),
                name = p.Name,
                faceValueIqd = p.FaceValueIqd,
                priceIqd = p.PriceIqd,
            }),
            transactions = seed.Transactions.Select(t => new
            {
                idempotencyKey = t.IdempotencyKey,
                reference = t.Reference,
                user = t.User!.Phone,
                from = t.FromAccount!.User!.Phone,
                type = t.Type.ToString(),
                status = t.Status.ToString(),
                errorCode = t.ErrorCode,
                amountIqd = t.AmountIqd,
                feeIqd = t.FeeIqd,
                totalIqd = t.TotalIqd,
                to = t.ToAccount?.User?.Phone,
                billAccount = t.BillAccount?.AccountNumber,
                product = t.Product?.Name,
                createdAt = t.CreatedAt,
                completedAt = t.CompletedAt,
            }),
            orders = seed.Orders.Select(o => new
            {
                user = o.User!.Phone,
                product = o.Product!.Name,
                transaction = o.Transaction!.IdempotencyKey,
                code = o.Code,
                createdAt = o.CreatedAt,
            }),
        };

        var json = JsonSerializer.Serialize(canonical, HashJsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string HashSeedPin() => PinHasher.Hash("1234");

    private static void InsertInOrder<T>(DbContext db, IEnumerable<T> rows) where T : class
    {
        foreach (var row in rows)
        {
            db.Add(row);
            db.SaveChanges();
        }
    }

    private sealed record SeedGraph(
        List<User> Users,
        List<Biller> Billers,
        List<Product> Products,
        List<Transaction> Transactions,
        List<Order> Orders);

    private static SeedGraph Build(Func<string> pinHash)
    {
        // Users 1 to 12, in identity order. Display names are the registered names.
        List<User> users =
        [
            NewUser("07700001001", "أحمد كريم جاسم", 300000, pinHash),
            NewUser("07700001002", "سارة عبد الله حسن", 1250000, pinHash),
            NewUser("07700001003", "عمر سعد محمود", 0, pinHash),
            NewUser("07700001004", "محمود كريم جاسم", 80000, pinHash),
            NewUser("07700001005", "علي حسن كاظم", 45000, pinHash),
            NewUser("07700001006", "علي كريم عبود", 120000, pinHash),
            NewUser("07700001007", "حسن علي رشيد", 10000, pinHash),
            NewUser("07700001008", "حسين علي رشيد", 10000, pinHash),
            NewUser("07700001009", "فاطمة جاسم محمد", 500000, pinHash),
            NewUser("07700001010", "نور صباح فاضل", 25000, pinHash),
            NewUser("07700001011", "زينب طالب حميد", 60000, pinHash),
            NewUser("07700001012", "كاظم جبار علي", 15000, pinHash),
        ];

        User UserNo(int id) => users[id - 1];
        Account AccountOf(int id) => users[id - 1].Accounts.Single(a => a.IsDefault);

        var ahmed = UserNo(1);
        var sara = UserNo(2);

        // Contacts. User 1 has a planted duplicate (two contacts saved as Ali) and a planted near name (Hasan, Husain).
        AddContacts(ahmed,
            ("حمودي", 4),
            ("علي", 5),
            ("علي", 6),
            ("حسن", 7),
            ("حسين", 8),
            ("ماما", 9),
            ("نور", 10),
            ("ام محمد", 11),
            ("ابو علي", 12),
            ("سارة", 2));
        AddContacts(sara,
            ("احمد", 1),
            ("عمر", 3));

        void AddContacts(User owner, params (string SavedName, int UserId)[] entries)
        {
            foreach (var (savedName, userId) in entries)
                owner.Contacts.Add(new Contact { Owner = owner, SavedName = savedName, Phone = UserNo(userId).Phone });
        }

        // Aliases of user 1: one alias, one account (D15).
        foreach (var (text, userId) in new[] { ("اخوي", 4), ("البيت", 9), ("الدكان", 12) })
            ahmed.Aliases.Add(new Alias { Owner = ahmed, Text = text, TargetAccount = AccountOf(userId) });

        // Billers.
        var electricity = new Biller { Code = "electricity", Name = "الكهرباء" };
        var water = new Biller { Code = "water", Name = "الماء" };
        var internet = new Biller { Code = "internet", Name = "نور نت" };
        List<Biller> billers = [electricity, water, internet];

        // Bill accounts.
        var ahmedElectricityHome = NewBill(ahmed, electricity, "البيت", "10000000001", 45000, new DateOnly(2026, 10, 25));
        NewBill(ahmed, electricity, "الدكان", "10000000002", 120000, new DateOnly(2026, 10, 20));
        var ahmedWaterHome = NewBill(ahmed, water, "البيت", "20000000001", 15000, null);
        var ahmedInternetHome = NewBill(ahmed, internet, "البيت", "30000000001", 0, null);
        NewBill(sara, electricity, "البيت", "10000000003", 60000, null);

        // Store catalogue: Provider, Type, Name, FaceValue, Price.
        List<Product> products =
        [
            new() { Provider = "Zain", Type = ProductType.Credit, Name = "رصيد زين ٥٠٠٠", FaceValueIqd = 5000, PriceIqd = 5000 },
            new() { Provider = "Zain", Type = ProductType.Credit, Name = "رصيد زين ١٠٠٠٠", FaceValueIqd = 10000, PriceIqd = 10000 },
            new() { Provider = "Zain", Type = ProductType.Internet, Name = "انترنت زين ١٠ جيجا", FaceValueIqd = 0, PriceIqd = 15000 },
            new() { Provider = "Asiacell", Type = ProductType.Credit, Name = "رصيد آسياسيل ٥٠٠٠", FaceValueIqd = 5000, PriceIqd = 5000 },
            new() { Provider = "Asiacell", Type = ProductType.Credit, Name = "رصيد آسياسيل ١٠٠٠٠", FaceValueIqd = 10000, PriceIqd = 10000 },
            new() { Provider = "Asiacell", Type = ProductType.Internet, Name = "انترنت آسياسيل ١٠ جيجا", FaceValueIqd = 0, PriceIqd = 15000 },
            new() { Provider = "Korek", Type = ProductType.Credit, Name = "رصيد كورك ٥٠٠٠", FaceValueIqd = 5000, PriceIqd = 5000 },
            new() { Provider = "Korek", Type = ProductType.Internet, Name = "انترنت كورك ١٠ جيجا", FaceValueIqd = 0, PriceIqd = 15000 },
        ];
        var zainCredit5000 = products[0];
        var asiacellInternet = products[5];

        // History. Balances above are frozen values; they are not recomputed from these rows.
        var fees = new WalletOptions();
        List<Transaction> transactions = [];
        List<Order> orders = [];
        var keyCounters = new Dictionary<User, int>();

        Transaction History(User payer, TransactionType type, long amountIqd, int daysAgo,
            Account? to = null, BillAccount? bill = null, Product? product = null)
        {
            keyCounters[payer] = keyCounters.GetValueOrDefault(payer) + 1;
            var fee = type switch
            {
                TransactionType.Transfer => fees.TransferFeeIqd,
                TransactionType.Bill => fees.BillFeeIqd,
                _ => fees.PurchaseFeeIqd,
            };
            var createdAt = Anchor.AddDays(-daysAgo);
            var row = new Transaction
            {
                IdempotencyKey = $"seed-u{users.IndexOf(payer) + 1}-{keyCounters[payer]:D3}",
                Reference = $"ZC-{transactions.Count + 1:D10}",
                User = payer,
                FromAccount = payer.Accounts.Single(a => a.IsDefault),
                Type = type,
                Status = TransactionStatus.Done,
                AmountIqd = amountIqd,
                FeeIqd = fee,
                TotalIqd = amountIqd + fee,
                ToAccount = to,
                BillAccount = bill,
                Product = product,
                CreatedAt = createdAt,
                CompletedAt = createdAt,
            };
            transactions.Add(row);
            return row;
        }

        void Purchase(User payer, Product product, int daysAgo, string code)
        {
            var row = History(payer, TransactionType.Purchase, product.PriceIqd, daysAgo, product: product);
            orders.Add(new Order { User = payer, Product = product, Transaction = row, Code = code, CreatedAt = row.CreatedAt });
        }

        // User 1: transfers.
        History(ahmed, TransactionType.Transfer, 50000, 90, to: AccountOf(4));
        History(ahmed, TransactionType.Transfer, 50000, 60, to: AccountOf(4));
        History(ahmed, TransactionType.Transfer, 50000, 30, to: AccountOf(4));
        History(ahmed, TransactionType.Transfer, 25000, 7, to: AccountOf(4));
        History(ahmed, TransactionType.Transfer, 20000, 45, to: AccountOf(5));
        History(ahmed, TransactionType.Transfer, 30000, 20, to: AccountOf(6));
        History(ahmed, TransactionType.Transfer, 100000, 75, to: AccountOf(9));
        History(ahmed, TransactionType.Transfer, 100000, 40, to: AccountOf(9));
        History(ahmed, TransactionType.Transfer, 100000, 10, to: AccountOf(9));
        History(ahmed, TransactionType.Transfer, 15000, 3, to: AccountOf(10));
        History(ahmed, TransactionType.Transfer, 10000, 50, to: AccountOf(11));

        // User 1: bills.
        History(ahmed, TransactionType.Bill, 42000, 80, bill: ahmedElectricityHome);
        History(ahmed, TransactionType.Bill, 44000, 50, bill: ahmedElectricityHome);
        History(ahmed, TransactionType.Bill, 46000, 18, bill: ahmedElectricityHome);
        History(ahmed, TransactionType.Bill, 15000, 48, bill: ahmedWaterHome);
        History(ahmed, TransactionType.Bill, 25000, 35, bill: ahmedInternetHome);

        // User 1: purchases, each with an order and its code.
        Purchase(ahmed, zainCredit5000, 28, "1234567890001");
        Purchase(ahmed, asiacellInternet, 12, "1234567890002");

        // User 2: one transfer to user 1.
        History(sara, TransactionType.Transfer, 200000, 15, to: AccountOf(1));

        return new SeedGraph(users, billers, products, transactions, orders);
    }

    private static User NewUser(string phone, string displayName, long balanceIqd, Func<string> pinHash)
    {
        var user = new User { Phone = phone, DisplayName = displayName, PinHash = pinHash() };
        user.Accounts.Add(new Account { User = user, IsDefault = true, BalanceIqd = balanceIqd });
        return user;
    }

    private static BillAccount NewBill(User user, Biller biller, string label, string accountNumber, long amountDueIqd, DateOnly? dueDate)
    {
        var bill = new BillAccount
        {
            User = user,
            Biller = biller,
            Label = label,
            AccountNumber = accountNumber,
            AmountDueIqd = amountDueIqd,
            DueDate = dueDate,
        };
        user.BillAccounts.Add(bill);
        return bill;
    }
}
