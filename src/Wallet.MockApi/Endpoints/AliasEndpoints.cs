using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Endpoints;

/// <summary>The problem codes of the alias endpoints (milestone 9a).</summary>
public static class AliasErrors
{
    public const string UnknownAlias = "unknown_alias";
    public const string AliasExists = "alias_exists";
    public const string AliasTextInvalid = "alias_text_invalid";
}

/// <summary>
/// The two alias write routes (milestone 9a): save one alias of one account (D15) and delete one. They move no money,
/// so they do not take the <see cref="Services.WalletGate"/>; the client key is checked by the group they are mapped on.
/// Aliases are read back with <c>GET /users/{id}/contacts</c>.
/// </summary>
public static class AliasEndpoints
{
    /// <summary>The longest alias text the wallet stores (the <c>Aliases.Text</c> column), after trimming.</summary>
    public const int MaxTextLength = WalletDbContext.AliasLength;

    private const int SqlDuplicateKeyRow = 2601;
    private const int SqlUniqueConstraint = 2627;

    public static IEndpointRouteBuilder MapAliasEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/users/{id:int}/aliases", CreateAsync)
            .WithName("CreateAlias")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete("/users/{id:int}/aliases/{aliasId:int}", DeleteAsync)
            .WithName("DeleteAlias")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Saves the trimmed text as the user's alias of the target account. Checks, in order: the user exists, the text is
    /// not blank and not longer than <see cref="MaxTextLength"/>, the account exists, and the user has no alias with
    /// that text yet. The unique index on <c>(OwnerUserId, Text)</c> is the last guard against a racing save.
    /// </summary>
    private static async Task<Results<Created<AliasItem>, ProblemHttpResult>> CreateAsync(
        int id, AliasRequest request, WalletDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
            return WalletProblems.UserNotFound();

        // A body without "text" binds it as null, whatever the record says.
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength)
            return WalletProblems.AliasTextInvalid();

        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == request.TargetAccountId)
            .Select(a => new { a.Id, a.Phone, a.RegisteredName, a.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
            return WalletProblems.AccountNotFound();

        // Compared in the database, under the same collation as the unique index.
        if (await db.Aliases.AnyAsync(a => a.OwnerUserId == id && a.Text == text, cancellationToken))
            return WalletProblems.AliasExists();

        var alias = new Alias { OwnerUserId = id, Text = text, TargetAccountId = account.Id };
        db.Aliases.Add(alias);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return WalletProblems.AliasExists();
        }

        return TypedResults.Created(
            $"/users/{id}/contacts",
            new AliasItem(
                alias.Id, alias.Text, account.Id, account.Phone, account.RegisteredName, AccountStatusNames.Of(account.Status)));
    }

    /// <summary>Deletes one alias of the user. An alias of another user is answered as unknown, never as forbidden.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        int id, int aliasId, WalletDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
            return WalletProblems.UserNotFound();

        var deleted = await db.Aliases
            .Where(a => a.Id == aliasId && a.OwnerUserId == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? WalletProblems.AliasNotFound() : TypedResults.NoContent();
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is SqlException { Number: SqlDuplicateKeyRow or SqlUniqueConstraint };
}
