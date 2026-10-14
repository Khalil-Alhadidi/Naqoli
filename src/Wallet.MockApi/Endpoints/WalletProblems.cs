using Microsoft.AspNetCore.Http.HttpResults;
using Wallet.MockApi.Services;

namespace Wallet.MockApi.Endpoints;

/// <summary>
/// The one way the payment endpoints build a ProblemDetails: a fixed English title per code, and the code itself
/// as the <c>code</c> extension, plus the figures some codes carry. Nothing from the request is echoed.
/// </summary>
internal static class WalletProblems
{
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        [PaymentErrors.MissingIdempotencyKey] = "Idempotency key missing or too long",
        [PaymentErrors.InvalidType] = "Unknown payment type",
        [PaymentErrors.UnknownUser] = "Unknown user",
        [PaymentErrors.UnknownRecipient] = "Unknown recipient",
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
    };

    public static ProblemHttpResult From(PaymentRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        var extensions = new Dictionary<string, object?> { ["code"] = refusal.Code };
        if (refusal.AmountDueIqd is { } amountDue)
            extensions["amountDueIqd"] = amountDue;
        if (refusal.AttemptsLeft is { } attemptsLeft)
            extensions["attemptsLeft"] = attemptsLeft;

        return TypedResults.Problem(
            statusCode: refusal.StatusCode,
            title: Titles.GetValueOrDefault(refusal.Code, "Payment refused"),
            extensions: extensions);
    }

    public static ProblemHttpResult Create(int statusCode, string code) => From(new PaymentRefusal(statusCode, code));
}
