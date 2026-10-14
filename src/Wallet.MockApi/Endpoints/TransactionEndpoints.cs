using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Services;

namespace Wallet.MockApi.Endpoints;

/// <summary>
/// Quotes, execution and lookup by idempotency key (architecture, section 7). <c>POST /transactions</c> is the only
/// endpoint that moves money. These handlers bind and map; the rules live in <see cref="PaymentValidator"/> and
/// <see cref="TransactionService"/>.
/// </summary>
public static class TransactionEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/quotes", QuoteAsync)
            .WithName("Quote")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/transactions", ExecuteAsync)
            .WithName("Execute")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        app.MapGet("/transactions/by-key/{key}", GetByKeyAsync)
            .WithName("GetTransactionByKey")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<QuoteResponse>, ProblemHttpResult>> QuoteAsync(
        PaymentRequest request, PaymentValidator validator, CancellationToken cancellationToken)
    {
        var check = await validator.ValidateAsync(request, cancellationToken);
        return check.IsValid ? TypedResults.Ok(check.Plan.ToQuote()) : WalletProblems.From(check.Refusal);
    }

    private static async Task<Results<Created<TransactionResponse>, Ok<TransactionResponse>, ProblemHttpResult>> ExecuteAsync(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        PaymentRequest request,
        TransactionService transactions,
        CancellationToken cancellationToken)
    {
        // Both checks come before the fault switch, so a 400 never uses up a once fault.
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return WalletProblems.Create(StatusCodes.Status400BadRequest, PaymentErrors.MissingIdempotencyKey);
        if (!TransactionService.IsValidIdempotencyKey(idempotencyKey))
            return WalletProblems.Create(StatusCodes.Status400BadRequest, PaymentErrors.InvalidIdempotencyKey);

        var result = await transactions.ExecuteAsync(idempotencyKey, request, cancellationToken);
        return result switch
        {
            ExecuteResult.Executed executed => TypedResults.Created(ByKeyPath(idempotencyKey), executed.Response),
            ExecuteResult.Replayed replayed => TypedResults.Ok(replayed.Response),
            ExecuteResult.Refused refused => WalletProblems.From(refused.Refusal),
            _ => throw new InvalidOperationException("Unknown execute result."),
        };
    }

    private static async Task<Results<Ok<TransactionResponse>, ProblemHttpResult>> GetByKeyAsync(
        string key, [FromQuery] int? userId, TransactionService transactions, CancellationToken cancellationToken)
    {
        // The caller names whose key it is; another user's key is simply unknown.
        if (userId is not { } owner)
            return WalletProblems.Create(StatusCodes.Status400BadRequest, PaymentErrors.MissingUserId);

        // A malformed or seeded key can never be a caller's, so it is simply unknown.
        var found = TransactionService.IsValidIdempotencyKey(key)
            ? await transactions.FindAsync(key, owner, cancellationToken)
            : null;
        return found is null
            ? WalletProblems.Create(StatusCodes.Status404NotFound, PaymentErrors.UnknownKey)
            : TypedResults.Ok(found);
    }

    private static string ByKeyPath(string key) => "/transactions/by-key/" + Uri.EscapeDataString(key);
}
