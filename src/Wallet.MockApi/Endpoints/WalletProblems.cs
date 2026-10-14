using Microsoft.AspNetCore.Http.HttpResults;
using Wallet.MockApi.Services;

namespace Wallet.MockApi.Endpoints;

/// <summary>
/// The one way the read, payment and admin endpoints build a ProblemDetails: a fixed English title per code, and the code itself
/// as the <c>code</c> extension, plus the figures some codes carry. Nothing from the request is echoed.
/// </summary>
internal static class WalletProblems
{
    /// <summary>The code of the 404 a phone number with no wallet account behind it gets.</summary>
    public const string UnknownAccount = "unknown_account";

    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        [PaymentErrors.MissingIdempotencyKey] = "Idempotency key missing",
        [PaymentErrors.InvalidIdempotencyKey] = "Idempotency key malformed",
        [PaymentErrors.InvalidType] = "Unknown payment type",
        [PaymentErrors.UnknownUser] = "Unknown user",
        [PaymentErrors.UnknownRecipient] = "Unknown recipient",
        [PaymentErrors.AccountClosed] = "Account is closed",
        [PaymentErrors.SameAccount] = "Payer and recipient are the same account",
        [PaymentErrors.InvalidAmount] = "Invalid amount",
        [PaymentErrors.NothingDue] = "Nothing is due on this bill",
        [PaymentErrors.BillsPaidInFull] = "Bills are paid in full",
        [PaymentErrors.UnknownProduct] = "Unknown product",
        [PaymentErrors.InsufficientFunds] = "Insufficient funds",
        [PaymentErrors.PinRequired] = "PIN required",
        [PaymentErrors.PinInvalid] = "PIN incorrect",
        [PaymentErrors.PinLocked] = "PIN locked",
        [PaymentErrors.InProgress] = "Transaction in progress",
        [PaymentErrors.UnknownKey] = "No transaction has this idempotency key",
        [PaymentErrors.IdempotencyKeyReused] = "Idempotency key reused with a different request",
        [PaymentErrors.MissingUserId] = "User id required",
        [PaymentErrors.PinMalformed] = "PIN must be 4 to 6 digits",
        [FaultErrors.InjectedError] = "Injected error",
        [FaultErrors.InjectedTimeout] = "Injected timeout",
        [AdminErrors.AdminKeyRequired] = "Admin key required",
        [ClientErrors.ClientKeyRequired] = "Client key required",
        [AdminErrors.InvalidFaultMode] = "Unknown fault mode",
        [AdminErrors.InvalidFaultDelay] = "Invalid fault delay",
    };

    /// <summary>
    /// The 404 every <c>/users/...</c> read route returns for a user that does not exist: code <c>unknown_user</c>
    /// (the same code quote and execute refuse an unknown payer with, there as 422) and the title <c>User not found</c>.
    /// </summary>
    public static ProblemHttpResult UserNotFound()
        => From(new PaymentRefusal(StatusCodes.Status404NotFound, PaymentErrors.UnknownUser), "User not found");

    /// <summary>
    /// The 404 <c>GET /accounts/by-phone/{phone}</c> returns when no wallet account has that number: code
    /// <c>unknown_account</c> and the title <c>Account not found</c>.
    /// </summary>
    public static ProblemHttpResult AccountNotFound()
        => From(new PaymentRefusal(StatusCodes.Status404NotFound, UnknownAccount), "Account not found");

    public static ProblemHttpResult From(PaymentRefusal refusal) => From(refusal, title: null);

    public static ProblemHttpResult Create(int statusCode, string code) => From(new PaymentRefusal(statusCode, code));

    private static ProblemHttpResult From(PaymentRefusal refusal, string? title)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        var extensions = new Dictionary<string, object?> { ["code"] = refusal.Code };
        if (refusal.AmountDueIqd is { } amountDue)
            extensions["amountDueIqd"] = amountDue;
        if (refusal.AttemptsLeft is { } attemptsLeft)
            extensions["attemptsLeft"] = attemptsLeft;

        return TypedResults.Problem(
            statusCode: refusal.StatusCode,
            title: title ?? Titles.GetValueOrDefault(refusal.Code, "Payment refused"),
            extensions: extensions);
    }
}
