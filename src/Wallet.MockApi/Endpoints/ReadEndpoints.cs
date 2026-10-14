using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Endpoints;

/// <summary>
/// The read-only endpoints of the mock wallet: users, contacts and aliases, bills, balance, history,
/// the store catalogue and orders (architecture, section 7). None of them changes data.
/// </summary>
public static class ReadEndpoints
{
    /// <summary>History items returned when the caller does not ask for a number.</summary>
    public const int DefaultHistoryTake = 50;

    /// <summary>The most history items one call returns; larger requests are clamped to this.</summary>
    public const int MaxHistoryTake = 200;

    public static IEndpointRouteBuilder MapReadEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/users/{id:int}", GetUserAsync)
            .WithName("GetUser")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/users/by-phone/{phone}", GetUserByPhoneAsync)
            .WithName("GetUserByPhone")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/users/{id:int}/contacts", GetContactsAsync)
            .WithName("GetContacts")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/users/{id:int}/bills", GetBillsAsync)
            .WithName("GetBills")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/users/{id:int}/balance", GetBalanceAsync)
            .WithName("GetBalance")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/users/{id:int}/history", GetHistoryAsync)
            .WithName("GetHistory")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/store/products", GetProductsAsync)
            .WithName("GetProducts");

        app.MapGet("/users/{id:int}/orders", GetOrdersAsync)
            .WithName("GetOrders")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>The one 404 every <c>/users/...</c> route returns for a user that does not exist.</summary>
    internal static ProblemHttpResult UserNotFound()
        => TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "User not found",
            detail: "No wallet user matches the request.");

    private static async Task<Results<Ok<UserSummary>, ProblemHttpResult>> GetUserAsync(
        int id, WalletDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserSummary(u.Id, u.Phone, u.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);

        return user is null ? UserNotFound() : TypedResults.Ok(user);
    }

    private static async Task<Results<Ok<UserSummary>, ProblemHttpResult>> GetUserByPhoneAsync(
        string phone, WalletDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Phone == phone)
            .Select(u => new UserSummary(u.Id, u.Phone, u.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);

        return user is null ? UserNotFound() : TypedResults.Ok(user);
    }

    private static async Task<Results<Ok<ContactsResponse>, ProblemHttpResult>> GetContactsAsync(
        int id, string? q, WalletDbContext db, CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(db, id, cancellationToken))
            return UserNotFound();

        // Raw, case-insensitive substring. Arabic normalising is the agent's job, not the wallet's.
        var term = string.IsNullOrEmpty(q) ? null : q.ToLowerInvariant();

        var contactRows = db.Contacts.AsNoTracking().Where(c => c.OwnerUserId == id);
        if (term is not null)
            contactRows = contactRows.Where(c => c.SavedName.ToLower().Contains(term));

        var contacts = await (
            from contact in contactRows
            join user in db.Users on contact.Phone equals user.Phone
            join account in db.Accounts on user.Id equals account.UserId
            where account.IsDefault
            orderby contact.Id
            select new { contact.Id, contact.SavedName, contact.Phone, user.DisplayName, AccountId = account.Id })
            .ToListAsync(cancellationToken);

        var aliasRows = db.Aliases.AsNoTracking().Where(a => a.OwnerUserId == id);
        if (term is not null)
            aliasRows = aliasRows.Where(a => a.Text.ToLower().Contains(term));

        var aliases = await aliasRows
            .OrderBy(a => a.Id)
            .Select(a => new AliasItem(a.Id, a.Text, a.TargetAccountId, a.TargetAccount!.User!.Phone, a.TargetAccount.User.DisplayName))
            .ToListAsync(cancellationToken);

        var lastSent = await LastDoneTransfersAsync(
            db, id, contacts.Select(c => c.AccountId).Distinct().ToList(), cancellationToken);

        var items = contacts
            .Select(c =>
            {
                var last = lastSent.GetValueOrDefault(c.AccountId);
                return new ContactItem(
                    c.Id, c.SavedName, c.Phone, c.DisplayName, c.AccountId, last?.CreatedAt, last?.AmountIqd);
            })
            .ToList();

        return TypedResults.Ok(new ContactsResponse(items, aliases));
    }

    private static async Task<Results<Ok<List<BillItem>>, ProblemHttpResult>> GetBillsAsync(
        int id, WalletDbContext db, CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(db, id, cancellationToken))
            return UserNotFound();

        var bills = await db.BillAccounts.AsNoTracking()
            .Where(b => b.UserId == id)
            .OrderBy(b => b.Id)
            .Select(b => new BillItem(
                b.Id, b.Biller!.Code, b.Biller.Name, b.Label, b.AccountNumber, b.AmountDueIqd, b.DueDate))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(bills);
    }

    private static async Task<Results<Ok<BalanceResponse>, ProblemHttpResult>> GetBalanceAsync(
        int id, WalletDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        // Every user has exactly one default account (D10), so no default account means no such user.
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.UserId == id && a.IsDefault)
            .Select(a => new { a.Id, a.BalanceIqd })
            .SingleOrDefaultAsync(cancellationToken);

        return account is null
            ? UserNotFound()
            : TypedResults.Ok(new BalanceResponse(account.Id, account.BalanceIqd, clock.GetUtcNow()));
    }

    private static async Task<Results<Ok<List<HistoryItem>>, ProblemHttpResult>> GetHistoryAsync(
        int id, string? to, DateTimeOffset? since, int? take, WalletDbContext db, CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(db, id, cancellationToken))
            return UserNotFound();

        var limit = Math.Clamp(take ?? DefaultHistoryTake, 1, MaxHistoryTake);

        // The transactions this user paid, of every status.
        var rows = db.Transactions.AsNoTracking().Where(t => t.UserId == id);
        if (!string.IsNullOrEmpty(to))
        {
            rows = rows.Where(t => t.Type == TransactionType.Transfer
                && t.ToAccount!.IsDefault
                && t.ToAccount.User!.Phone == to);
        }

        if (since is { } from)
            rows = rows.Where(t => t.CreatedAt >= from);

        var history = await rows
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(limit)
            .Select(t => new
            {
                t.Id,
                t.Reference,
                t.Type,
                t.Status,
                t.AmountIqd,
                t.FeeIqd,
                t.TotalIqd,
                CounterpartyPhone = t.Type == TransactionType.Transfer ? t.ToAccount!.User!.Phone : null,
                CounterpartyName = t.Type == TransactionType.Transfer ? t.ToAccount!.User!.DisplayName : null,
                t.BillAccountId,
                t.ProductId,
                t.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(history
            .Select(t => new HistoryItem(
                t.Id,
                t.Reference ?? string.Empty,
                t.Type.ToString(),
                t.Status.ToString(),
                t.AmountIqd,
                t.FeeIqd,
                t.TotalIqd,
                t.CounterpartyPhone,
                t.CounterpartyName,
                t.BillAccountId,
                t.ProductId,
                t.CreatedAt))
            .ToList());
    }

    private static async Task<Ok<List<ProductItem>>> GetProductsAsync(
        string? provider, string? type, long? value, WalletDbContext db, CancellationToken cancellationToken)
    {
        var rows = db.Products.AsNoTracking();

        if (!string.IsNullOrEmpty(provider))
        {
            var providerKey = provider.ToLowerInvariant();
            rows = rows.Where(p => p.Provider.ToLower() == providerKey);
        }

        if (!string.IsNullOrEmpty(type))
        {
            // Only an exact enum name matches (any case); numbers and unknown names match nothing.
            var name = Enum.GetNames<ProductType>()
                .FirstOrDefault(n => string.Equals(n, type, StringComparison.OrdinalIgnoreCase));
            if (name is null)
                return TypedResults.Ok(new List<ProductItem>());

            var productType = Enum.Parse<ProductType>(name);
            rows = rows.Where(p => p.Type == productType);
        }

        if (value is { } faceValue)
            rows = rows.Where(p => p.FaceValueIqd == faceValue);

        var products = await rows
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Provider, p.Type, p.Name, p.FaceValueIqd, p.PriceIqd })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(products
            .Select(p => new ProductItem(p.Id, p.Provider, p.Type.ToString(), p.Name, p.FaceValueIqd, p.PriceIqd))
            .ToList());
    }

    private static async Task<Results<Ok<List<OrderItem>>, ProblemHttpResult>> GetOrdersAsync(
        int id, WalletDbContext db, CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(db, id, cancellationToken))
            return UserNotFound();

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == id)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Select(o => new OrderItem(o.Id, o.TransactionId, o.ProductId, o.Product!.Name, o.Code, o.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(orders);
    }

    private static Task<bool> UserExistsAsync(WalletDbContext db, int id, CancellationToken cancellationToken)
        => db.Users.AnyAsync(u => u.Id == id, cancellationToken);

    /// <summary>
    /// For each of the given accounts, the latest <c>Done</c> transfer the user sent to it (D3: "when you last sent").
    /// </summary>
    private static async Task<Dictionary<int, LastTransfer>> LastDoneTransfersAsync(
        WalletDbContext db, int userId, List<int> accountIds, CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
            return [];

        var transfers = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId
                && t.Type == TransactionType.Transfer
                && t.Status == TransactionStatus.Done
                && t.ToAccountId != null
                && accountIds.Contains(t.ToAccountId.Value))
            .Select(t => new { AccountId = t.ToAccountId!.Value, t.Id, t.CreatedAt, t.AmountIqd })
            .ToListAsync(cancellationToken);

        return transfers
            .GroupBy(t => t.AccountId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                    .Select(t => new LastTransfer(t.CreatedAt, t.AmountIqd))
                    .First());
    }

    private sealed record LastTransfer(DateTimeOffset CreatedAt, long AmountIqd);
}
