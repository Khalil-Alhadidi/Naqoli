using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Data;

/// <summary>Returns the wallet database to the frozen seed, with identity values starting again at 1.</summary>
public static class SeedReset
{
    /// <summary>
    /// In one database transaction: deletes every row in dependency order, reseeds each identity column,
    /// then applies <see cref="SeedData"/>. Pure data; no HTTP.
    /// </summary>
    public static void Reset(WalletDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        using var transaction = db.Database.BeginTransaction();

        db.Orders.ExecuteDelete();
        db.Transactions.ExecuteDelete();
        db.BillAccounts.ExecuteDelete();
        db.Aliases.ExecuteDelete();
        db.Contacts.ExecuteDelete();
        db.Accounts.ExecuteDelete();
        db.Users.ExecuteDelete();
        db.Products.ExecuteDelete();
        db.Billers.ExecuteDelete();

        Type[] identityTables =
        [
            typeof(Order), typeof(Transaction), typeof(BillAccount), typeof(Alias), typeof(Contact),
            typeof(Account), typeof(User), typeof(Product), typeof(Biller),
        ];
        foreach (var entity in identityTables)
            Reseed(db, entity);

        db.ChangeTracker.Clear();
        SeedData.Apply(db);

        transaction.Commit();
    }

    // Table names come from the EF model, never from input. RESEED 0 makes the next row 1 only on a table
    // that has held rows; on a table that never had one it would make the next row 0, so such a table is skipped.
    private static void Reseed(WalletDbContext db, Type entity)
    {
        var entityType = db.Model.FindEntityType(entity)
            ?? throw new InvalidOperationException($"{entity.Name} is not in the wallet model.");
        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException($"{entity.Name} has no table.");
        var name = $"[{entityType.GetSchema() ?? "dbo"}].[{table}]";

        var sql = string.Concat(
            "IF EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id = OBJECT_ID(N'", name, "') AND last_value IS NOT NULL) ",
            "DBCC CHECKIDENT ('", name, "', RESEED, 0) WITH NO_INFOMSGS;");
        db.Database.ExecuteSqlRaw(sql);
    }
}
