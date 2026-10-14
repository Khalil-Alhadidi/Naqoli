using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
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
/// The fault switch and the reset (architecture, section 7). Every route is in the <c>/admin</c> group, behind
/// <see cref="AdminKeyFilter"/>.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>The longest delay a fault may hold a response, as for <see cref="WalletOptions.FaultDelayMs"/>.</summary>
    public const int MaxFaultDelayMs = 600_000;

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

        return app;
    }

    private static Ok<FaultState> GetFaults(FaultSwitch faults) => TypedResults.Ok(faults.Get().ToState());

    private static Results<Ok<FaultState>, ProblemHttpResult> SetFaults(FaultRequest request, FaultSwitch faults)
    {
        if (!FaultModes.TryParse(request.Mode, out var mode))
            return WalletProblems.Create(StatusCodes.Status422UnprocessableEntity, AdminErrors.InvalidFaultMode);
        if (request.DelayMs is < 0 or > MaxFaultDelayMs)
            return WalletProblems.Create(StatusCodes.Status422UnprocessableEntity, AdminErrors.InvalidFaultDelay);

        return TypedResults.Ok(faults.Set(mode, request.DelayMs, request.Once).ToState());
    }

    /// <summary>
    /// Holds the gate exclusive, so no payment and no other reset runs alongside; restores the seed and clears the
    /// fault switch; and reports the row counts read back from the database.
    /// </summary>
    private static async Task<Ok<ResetResponse>> ResetAsync(
        WalletDbContext db, FaultSwitch faults, WalletGate gate, CancellationToken cancellationToken)
    {
        using (await gate.EnterExclusiveAsync(cancellationToken))
        {
            SeedReset.Reset(db);
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
