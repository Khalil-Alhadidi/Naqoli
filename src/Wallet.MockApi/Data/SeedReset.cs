using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Data.Entities;
using Wallet.MockApi.Data.Seed;

namespace Wallet.MockApi.Data;

/// <summary>Returns the wallet database to the seed, with identity values starting again at 1.</summary>
public static class SeedReset
{
    /// <summary>
    /// In one database transaction: deletes every row in dependency order, reseeds each identity column,
    /// then applies <see cref="SeedData"/> built from <paramref name="seed"/>, with history dated from <paramref name="now"/>.
    /// Pure data; no HTTP.
    /// </summary>
    /// <remarks>
    /// <see cref="SeedData.Apply"/> stays synchronous (it is shared with <c>migrate</c>) and runs inside this
    /// transaction; its PIN hashes are computed once per process, so it does little more than insert rows.
    /// </remarks>
    public static async Task ResetAsync(WalletDbContext db, SeedSet seed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(seed);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Orders.ExecuteDeleteAsync(cancellationToken);
        await db.Transactions.ExecuteDeleteAsync(cancellationToken);
        await db.BillAccounts.ExecuteDeleteAsync(cancellationToken);
        await db.Aliases.ExecuteDeleteAsync(cancellationToken);
        await db.Contacts.ExecuteDeleteAsync(cancellationToken);
        await db.Accounts.ExecuteDeleteAsync(cancellationToken);
        await db.Users.ExecuteDeleteAsync(cancellationToken);
        await db.Products.ExecuteDeleteAsync(cancellationToken);
        await db.Billers.ExecuteDeleteAsync(cancellationToken);

        Type[] identityTables =
        [
            typeof(Order), typeof(Transaction), typeof(BillAccount), typeof(Alias), typeof(Contact),
            typeof(Account), typeof(User), typeof(Product), typeof(Biller),
        ];
        foreach (var entity in identityTables)
            await ReseedAsync(db, entity, cancellationToken);

        db.ChangeTracker.Clear();
        SeedData.Apply(db, seed, now);

        await transaction.CommitAsync(cancellationToken);
    }

    // Table names come from the EF model, never from input. RESEED 0 makes the next row 1 only on a table
    // that has held rows; on a table that never had one it would make the next row 0, so such a table is skipped.
    private static async Task ReseedAsync(WalletDbContext db, Type entity, CancellationToken cancellationToken)
    {
        var entityType = db.Model.FindEntityType(entity)
            ?? throw new InvalidOperationException($"{entity.Name} is not in the wallet model.");
        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException($"{entity.Name} has no table.");
        var name = $"[{entityType.GetSchema() ?? "dbo"}].[{table}]";

        var sql = string.Concat(
            "IF EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id = OBJECT_ID(N'", name, "') AND last_value IS NOT NULL) ",
            "DBCC CHECKIDENT ('", name, "', RESEED, 0) WITH NO_INFOMSGS;");
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
