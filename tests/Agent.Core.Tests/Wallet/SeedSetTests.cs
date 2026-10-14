using System.Globalization;
using Wallet.MockApi;
using Wallet.MockApi.Data.Seed;

namespace Agent.Core.Tests.Wallet;

/// <summary>Loads and validates the exported test-set seed files in <c>data/seed</c>. No database.</summary>
public class SeedSetTests
{
    internal static string SeedDirectory => Path.Combine(SeedHashTests.RepositoryRoot(), "data", "seed");

    internal static SeedSet Seed => SeedSet.Load(SeedDirectory);

    [Fact]
    public void LoadsTheEightFiles()
    {
        var seed = Seed;

        Assert.Equal(5, seed.Users.Count);
        Assert.Equal(42, seed.Accounts.Count);
        Assert.Equal(34, seed.Contacts.Count);
        Assert.Equal(4, seed.Billers.Count);
        Assert.Equal(11, seed.Bills.Count);
        Assert.Equal(15, seed.Products.Count);
        Assert.Equal(86, seed.History.Count);
        Assert.Equal("IQD", seed.Settings.Currency);
    }

    [Fact]
    public void EveryUserIsAnActiveAccountWithTheSameNameAndBalance()
    {
        var seed = Seed;
        var accounts = seed.Accounts.ToDictionary(a => a.Number, StringComparer.Ordinal);

        Assert.All(seed.Users, user =>
        {
            Assert.True(accounts.TryGetValue(user.Number, out var account), $"{user.Id} has no account");
            Assert.Equal("user", account.Kind);
            Assert.Equal("active", account.Status);
            Assert.Equal(user.Name, account.RegisteredName);
            Assert.Equal(user.Balance, account.Balance);
        });
    }

    [Fact]
    public void EveryContactNumberIsAnAccount()
    {
        var seed = Seed;
        var numbers = seed.Accounts.Select(a => a.Number).ToHashSet(StringComparer.Ordinal);
        var users = seed.Users.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);

        Assert.All(seed.Contacts, contact =>
        {
            Assert.Contains(contact.Number, numbers);
            Assert.Contains(contact.User, users);
        });
    }

    [Fact]
    public void EveryHistoryRowPointsAtSeedRows()
    {
        var seed = Seed;
        var users = seed.Users.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var numbers = seed.Accounts.Select(a => a.Number).ToHashSet(StringComparer.Ordinal);
        var bills = seed.Bills.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var products = seed.Products.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        Assert.All(seed.History, row =>
        {
            Assert.Contains(row.User, users);
            switch (row.Type)
            {
                case "transfer":
                    Assert.NotNull(row.ToNumber);
                    Assert.Contains(row.ToNumber, numbers);
                    break;
                case "bill":
                    Assert.NotNull(row.Bill);
                    Assert.Contains(row.Bill, bills);
                    break;
                case "purchase":
                    Assert.NotNull(row.Product);
                    Assert.Contains(row.Product, products);
                    break;
                default:
                    Assert.Fail($"{row.Id} has type {row.Type}");
                    break;
            }

            Assert.True(
                TimeOnly.TryParseExact(row.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                $"{row.Id} has time {row.Time}");
        });
    }

    [Fact]
    public void TwoAccountsAreClosed()
        => Assert.Equal(
            ["07805550120", "07805550193"],
            Seed.Accounts.Where(a => a.Status == "closed").Select(a => a.Number).Order(StringComparer.Ordinal));

    [Fact]
    public void NineContactsHaveAnAlias()
        => Assert.Equal(9, Seed.Contacts.Count(c => c.Alias is not null));

    [Fact]
    public void OnlyOneUserHasNotGivenConsent()
        => Assert.Equal("U5", Assert.Single(Seed.Users, u => !u.ConsentGiven).Id);

    [Fact]
    public void HistoryFeesMatchTheSettings()
    {
        var seed = Seed;

        Assert.All(seed.History, h => Assert.Equal(
            h.Type switch
            {
                "transfer" => seed.Settings.Fees.Transfer,
                "bill" => seed.Settings.Fees.Bill,
                _ => seed.Settings.Fees.Purchase,
            },
            h.Fee));
    }

    [Fact]
    public void WalletDefaultsMatchTheSettingsFile()
    {
        var options = new WalletOptions();
        var s = Seed.Settings;

        Assert.Equal(s.Fees.Transfer, options.TransferFeeIqd);
        Assert.Equal(s.Fees.Bill, options.BillFeeIqd);
        Assert.Equal(s.Fees.Purchase, options.PurchaseFeeIqd);
        Assert.Equal(s.Pin.RequiredAbove, options.PinRequiredAboveIqd);
        Assert.Equal(s.Pin.Attempts, options.MaxPinAttempts);
    }

    [Fact]
    public void MissingFileNamesTheFile()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var ex = Assert.Throws<SeedException>(() => SeedSet.Load(dir.FullName));
            Assert.Contains("users.json", ex.Message);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void UnknownPropertyIsRejected()
    {
        var dir = CopySeed();
        try
        {
            ReplaceFirst(dir, SeedFiles.Users, "\"id\": \"U1\"", "\"extra\": 1, \"id\": \"U1\"");

            var ex = Assert.Throws<SeedException>(() => SeedSet.Load(dir.FullName));
            Assert.StartsWith("users.json", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void DanglingContactNumberIsRejected()
    {
        var dir = CopySeed();
        try
        {
            ReplaceFirst(dir, SeedFiles.Contacts, "\"number\": \"07805550101\"", "\"number\": \"07805559999\"");

            var ex = Assert.Throws<SeedException>(() => SeedSet.Load(dir.FullName));
            Assert.StartsWith("contacts.json", ex.Message, StringComparison.Ordinal);
            Assert.Contains("S01", ex.Message);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void MissingRequiredPropertyIsRejected()
    {
        var dir = CopySeed();
        try
        {
            ReplaceFirst(dir, SeedFiles.Users, "\"number\": \"07805550001\",", "");

            var ex = Assert.Throws<SeedException>(() => SeedSet.Load(dir.FullName));
            Assert.StartsWith("users.json", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void NullRequiredValueIsRejected()
    {
        var dir = CopySeed();
        try
        {
            ReplaceFirst(dir, SeedFiles.Contacts, "\"id\": \"S01\"", "\"id\": null");

            var ex = Assert.Throws<SeedException>(() => SeedSet.Load(dir.FullName));
            Assert.StartsWith("contacts.json", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void ClosedUserAccountIsRejected()
    {
        var dir = CopySeed();
        try
        {
            // U1's own account.
            ReplaceFirstAfter(dir, SeedFiles.Accounts, "\"number\": \"07805550001\"", "\"status\": \"active\"", "\"status\": \"closed\"");

            var ex = Assert.Throws<SeedException>(() => SeedSet.Load(dir.FullName));
            Assert.StartsWith("accounts.json", ex.Message, StringComparison.Ordinal);
            Assert.Contains("07805550001", ex.Message);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void UserAccountWithoutAUserIsRejected()
    {
        var dir = CopySeed();
        try
        {
            ReplaceFirstAfter(dir, SeedFiles.Accounts, "\"number\": \"07805550190\"", "\"kind\": \"other\"", "\"kind\": \"user\"");

            var ex = Assert.Throws<SeedException>(() => SeedSet.Load(dir.FullName));
            Assert.StartsWith("accounts.json", ex.Message, StringComparison.Ordinal);
            Assert.Contains("07805550190", ex.Message);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    /// <summary>Copies the eight seed files to a new temporary directory. The caller deletes it.</summary>
    internal static DirectoryInfo CopySeed()
    {
        var dir = Directory.CreateTempSubdirectory();
        foreach (var file in SeedFiles.All)
            File.Copy(Path.Combine(SeedDirectory, file), Path.Combine(dir.FullName, file));
        return dir;
    }

    /// <summary>Replaces the first occurrence of <paramref name="oldValue"/> in one copied seed file.</summary>
    internal static void ReplaceFirst(DirectoryInfo dir, string file, string oldValue, string newValue)
    {
        var path = Path.Combine(dir.FullName, file);
        var text = File.ReadAllText(path);
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(index >= 0, $"{oldValue} not found in {file}");
        File.WriteAllText(path, string.Concat(text.AsSpan(0, index), newValue, text.AsSpan(index + oldValue.Length)));
    }

    /// <summary>
    /// Replaces the first occurrence of <paramref name="oldValue"/> that follows <paramref name="anchor"/> in one copied
    /// seed file.
    /// </summary>
    internal static void ReplaceFirstAfter(DirectoryInfo dir, string file, string anchor, string oldValue, string newValue)
    {
        var path = Path.Combine(dir.FullName, file);
        var text = File.ReadAllText(path);
        var at = text.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{anchor} not found in {file}");
        var index = text.IndexOf(oldValue, at, StringComparison.Ordinal);
        Assert.True(index >= 0, $"{oldValue} not found after {anchor} in {file}");
        File.WriteAllText(path, string.Concat(text.AsSpan(0, index), newValue, text.AsSpan(index + oldValue.Length)));
    }
}
