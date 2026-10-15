using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;
using Wallet.MockApi.Services;

namespace Wallet.MockApi.Endpoints;

/// <summary>
/// The read endpoints of the mock wallet: users, accounts by phone, contacts and aliases, bills, balance, history,
/// the store catalogue and orders (architecture, section 7), and the wallet's own limits (<c>/settings</c>). None of them changes data, except the consent route,
/// which belongs to the user resource and only sets the user's consent flag.
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

        // The one route here that writes: consent belongs to the user resource, so it sits with the user reads.
        app.MapPost("/users/{id:int}/consent", GiveConsentAsync)
            .WithName("GiveConsent")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/accounts/by-phone/{phone}", GetAccountByPhoneAsync)
            .WithName("GetAccountByPhone")
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

        app.MapGet("/settings", GetSettings)
            .WithName("GetSettings");

        return app;
    }

    /// <summary>The wallet's configured fees and PIN limits; no database read.</summary>
    private static Ok<SettingsResponse> GetSettings(IOptions<WalletOptions> options)
    {
        var o = options.Value;
        return TypedResults.Ok(new SettingsResponse(
            o.TransferFeeIqd, o.BillFeeIqd, o.PurchaseFeeIqd, o.PinRequiredAboveIqd, o.MaxPinAttempts));
    }

    private static async Task<Results<Ok<UserSummary>, ProblemHttpResult>> GetUserAsync(
        int id, WalletDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserSummary(u.Id, u.Code, u.Phone, u.DisplayName, u.ConsentGiven))
            .SingleOrDefaultAsync(cancellationToken);

        return user is null ? WalletProblems.UserNotFound() : TypedResults.Ok(user);
    }

    /// <summary>
    /// Records that the user has given consent. Giving it again changes nothing and also returns 204. The write takes
    /// the wallet gate shared, so a reset never interleaves with a consent write.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> GiveConsentAsync(
        int id, WalletDbContext db, WalletGate gate, CancellationToken cancellationToken)
    {
        int updated;
        using (await gate.EnterSharedAsync(cancellationToken))
        {
            updated = await db.Users
                .Where(u => u.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.ConsentGiven, true), cancellationToken);
        }

        return updated == 0 ? WalletProblems.UserNotFound() : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AccountSummary>, ProblemHttpResult>> GetAccountByPhoneAsync(
        string phone, WalletDbContext db, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Phone == phone)
            .Select(a => new { a.Id, a.Phone, a.RegisteredName, a.Status })
            .SingleOrDefaultAsync(cancellationToken);

        return account is null
            ? WalletProblems.AccountNotFound()
            : TypedResults.Ok(new AccountSummary(
                account.Id, account.Phone, account.RegisteredName, AccountStatusNames.Of(account.Status)));
    }

    private static async Task<Results<Ok<ContactsResponse>, ProblemHttpResult>> GetContactsAsync(
        int id, string? q, WalletDbContext db, CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(db, id, cancellationToken))
            return WalletProblems.UserNotFound();

        // Raw, case-insensitive substring. Arabic normalising is the agent's job, not the wallet's.
        var term = string.IsNullOrEmpty(q) ? null : q.ToLowerInvariant();

        var contactRows = db.Contacts.AsNoTracking().Where(c => c.OwnerUserId == id);
        if (term is not null)
            contactRows = contactRows.Where(c => c.SavedName.ToLower().Contains(term));

        var contacts = await (
            from contact in contactRows
            join account in db.Accounts on contact.Phone equals account.Phone
            orderby contact.Id
            select new
            {
                contact.Id,
                contact.Code,
                contact.SavedName,
                contact.Phone,
                account.RegisteredName,
                account.Status,
                AccountId = account.Id,
            })
            .ToListAsync(cancellationToken);

        var aliasRows = db.Aliases.AsNoTracking().Where(a => a.OwnerUserId == id);
        if (term is not null)
            aliasRows = aliasRows.Where(a => a.Text.ToLower().Contains(term));

        var aliasList = await aliasRows
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.Text,
                a.TargetAccountId,
                a.TargetAccount!.Phone,
                a.TargetAccount.RegisteredName,
                a.TargetAccount.Status,
            })
            .ToListAsync(cancellationToken);

        var aliases = aliasList
            .Select(a => new AliasItem(
                a.Id, a.Text, a.TargetAccountId, a.Phone, a.RegisteredName, AccountStatusNames.Of(a.Status)))
            .ToList();

        var lastSent = await LastDoneTransfersAsync(
            db, id, contacts.Select(c => c.AccountId).Distinct().ToList(), cancellationToken);

        var items = contacts
            .Select(c =>
            {
                var last = lastSent.GetValueOrDefault(c.AccountId);
                return new ContactItem(
                    c.Id,
                    c.Code,
                    c.SavedName,
                    c.Phone,
                    c.RegisteredName,
                    AccountStatusNames.Of(c.Status),
                    c.AccountId,
                    last?.CreatedAt,
                    last?.AmountIqd);
            })
            .ToList();

        return TypedResults.Ok(new ContactsResponse(items, aliases));
    }

    private static async Task<Results<Ok<List<BillItem>>, ProblemHttpResult>> GetBillsAsync(
        int id, WalletDbContext db, CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(db, id, cancellationToken))
            return WalletProblems.UserNotFound();

        var bills = await db.BillAccounts.AsNoTracking()
            .Where(b => b.UserId == id)
            .OrderBy(b => b.Id)
            .Select(b => new BillItem(
                b.Id, b.Code, b.Biller!.Code, b.Biller.Name, b.Biller.NameEn, b.Label, b.AccountNumber, b.AmountDueIqd))
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
            ? WalletProblems.UserNotFound()
            : TypedResults.Ok(new BalanceResponse(account.Id, account.BalanceIqd, clock.GetUtcNow()));
    }

    private static async Task<Results<Ok<List<HistoryItem>>, ProblemHttpResult>> GetHistoryAsync(
        int id, string? to, DateTimeOffset? since, int? take, WalletDbContext db, CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(db, id, cancellationToken))
            return WalletProblems.UserNotFound();

        var limit = Math.Clamp(take ?? DefaultHistoryTake, 1, MaxHistoryTake);

        // The transactions this user paid, of every status.
        var rows = db.Transactions.AsNoTracking().Where(t => t.UserId == id);
        if (!string.IsNullOrEmpty(to))
        {
            rows = rows.Where(t => t.Type == TransactionType.Transfer && t.ToAccount!.Phone == to);
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
                CounterpartyPhone = t.Type == TransactionType.Transfer ? t.ToAccount!.Phone : null,
                CounterpartyName = t.Type == TransactionType.Transfer ? t.ToAccount!.RegisteredName : null,
                CounterpartyAccountId = t.Type == TransactionType.Transfer ? t.ToAccountId : null,
                t.BillAccountId,
                t.ProductId,
                t.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(history
            .Select(t => new HistoryItem(
                t.Id,
                t.Reference ?? string.Empty,
                TransactionNames.Of(t.Type),
                TransactionNames.Of(t.Status),
                t.AmountIqd,
                t.FeeIqd,
                t.TotalIqd,
                t.CounterpartyPhone,
                t.CounterpartyName,
                t.CounterpartyAccountId,
                t.BillAccountId,
                t.ProductId,
                t.CreatedAt))
            .ToList());
    }

    private static async Task<Ok<List<ProductItem>>> GetProductsAsync(
        string? provider, string? type, long? value, WalletDbContext db, CancellationToken cancellationToken)
    {
        var rows = db.Products.AsNoTracking();

        // Providers are stored as lower-case codes, so lower-casing the query is enough to match any case.
        if (!string.IsNullOrEmpty(provider))
        {
            var providerCode = provider.ToLowerInvariant();
            rows = rows.Where(p => p.Provider == providerCode);
        }

        if (!string.IsNullOrEmpty(type))
        {
            // Only an exact type name matches (any case); numbers and unknown names match nothing.
            if (!ProductTypeNames.TryParse(type, out var productType))
                return TypedResults.Ok(new List<ProductItem>());

            rows = rows.Where(p => p.Type == productType);
        }

        if (value is { } faceValue)
            rows = rows.Where(p => p.FaceValueIqd == faceValue);

        var products = await rows
            .OrderBy(p => p.Id)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Provider,
                p.ProviderNameEn,
                p.ProviderNameAr,
                p.AlsoCalled,
                p.Type,
                p.FaceValueIqd,
                p.PriceIqd,
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(products
            .Select(p => new ProductItem(
                p.Id,
                p.Code,
                p.Provider,
                p.ProviderNameEn,
                p.ProviderNameAr,
                p.AlsoCalled,
                ProductTypeNames.Of(p.Type),
                p.FaceValueIqd,
                p.PriceIqd))
            .ToList());
    }

    private static async Task<Results<Ok<List<OrderItem>>, ProblemHttpResult>> GetOrdersAsync(
        int id, WalletDbContext db, CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(db, id, cancellationToken))
            return WalletProblems.UserNotFound();

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == id)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Select(o => new OrderItem(o.Id, o.TransactionId, o.ProductId, o.Product!.Code, o.Code, o.CreatedAt))
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

/// <summary>The lower-case names account statuses carry in JSON: <c>active</c> or <c>closed</c>.</summary>
public static class AccountStatusNames
{
    public static string Of(AccountStatus status) => status switch
    {
        AccountStatus.Active => "active",
        AccountStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}

/// <summary>The lower-case names product types carry in JSON, as transaction types and statuses do.</summary>
public static class ProductTypeNames
{
    public static string Of(ProductType type) => type switch
    {
        ProductType.Credit => "credit",
        ProductType.Internet => "internet",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Parses a type name, ignoring case. Numbers and unknown names are refused.</summary>
    public static bool TryParse(string? name, out ProductType type)
    {
        foreach (var candidate in Enum.GetValues<ProductType>())
        {
            if (string.Equals(name, Of(candidate), StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }
}
