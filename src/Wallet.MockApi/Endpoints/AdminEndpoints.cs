using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Seed;
using Wallet.MockApi.Services;

namespace Wallet.MockApi.Endpoints;

/// <summary>The problem codes of the admin endpoints.</summary>
public static class AdminErrors
{
    public const string AdminKeyRequired = "admin_key_required";
    public const string InvalidFaultMode = "invalid_fault_mode";
    public const string InvalidFaultDelay = "invalid_fault_delay";
}

/// <summary>
/// The fault switch, the reset (architecture, section 7) and the external debit (Khalil, 14 Oct 2026). Every route is in the <c>/admin</c> group, behind
/// <see cref="AdminKeyFilter"/>.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var admin = app.MapGroup("/admin")
            .AddEndpointFilter<AdminKeyFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        admin.MapGet("/faults", GetFaults).WithName("GetFaults");

        admin.MapPost("/faults", SetFaults)
            .WithName("SetFaults")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        admin.MapPost("/reset", ResetAsync).WithName("Reset");

        admin.MapPost("/debit", DebitAsync)
            .WithName("ExternalDebit")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static Ok<FaultState> GetFaults(FaultSwitch faults) => TypedResults.Ok(faults.Get().ToState());

    private static Results<Ok<FaultState>, ProblemHttpResult> SetFaults(FaultRequest request, FaultSwitch faults)
    {
        if (!FaultModes.TryParse(request.Mode, out var mode))
            return WalletProblems.Create(StatusCodes.Status422UnprocessableEntity, AdminErrors.InvalidFaultMode);
        if (request.DelayMs is < 0 or > WalletOptions.MaxFaultDelayMs)
            return WalletProblems.Create(StatusCodes.Status422UnprocessableEntity, AdminErrors.InvalidFaultDelay);

        return TypedResults.Ok(faults.Set(mode, request.DelayMs, request.Once).ToState());
    }

    /// <summary>
    /// Takes money from a user's default account from outside the agent: 201 with the <c>externalDebit</c> row, or 422
    /// <c>invalid_amount</c> (not above 0), <c>unknown_user</c>, <c>account_closed</c> or <c>insufficient_funds</c> with
    /// nothing written.
    /// </summary>
    private static async Task<Results<Created<TransactionResponse>, ProblemHttpResult>> DebitAsync(
        DebitRequest request, ExternalDebitService debits, CancellationToken cancellationToken)
        => await debits.DebitAsync(request, cancellationToken) switch
        {
            DebitResult.Debited debited => TypedResults.Created((string?)null, debited.Response),
            DebitResult.Refused refused => WalletProblems.From(refused.Refusal),
            _ => throw new InvalidOperationException("Unknown debit result."),
        };

    /// <summary>
    /// Holds the gate exclusive, so no payment and no other reset runs alongside; restores the seed and clears the
    /// fault switch; and reports the row counts read back from the database.
    /// </summary>
    private static async Task<Ok<ResetResponse>> ResetAsync(
        WalletDbContext db,
        SeedSet seed,
        TimeProvider clock,
        FaultSwitch faults,
        WalletGate gate,
        CancellationToken cancellationToken)
    {
        using (await gate.EnterExclusiveAsync(cancellationToken))
        {
            await SeedReset.ResetAsync(db, seed, clock.GetUtcNow(), cancellationToken);
            faults.Set(FaultMode.None, null, once: false);

            return TypedResults.Ok(new ResetResponse(
                await db.Users.CountAsync(cancellationToken),
                await db.Accounts.CountAsync(cancellationToken),
                await db.Contacts.CountAsync(cancellationToken),
                await db.Aliases.CountAsync(cancellationToken),
                await db.BillAccounts.CountAsync(cancellationToken),
                await db.Products.CountAsync(cancellationToken),
                await db.Transactions.CountAsync(cancellationToken),
                await db.Orders.CountAsync(cancellationToken)));
        }
    }
}
