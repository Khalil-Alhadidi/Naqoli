using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Services;

/// <summary>The machine-readable codes the wallet refuses a payment with. Each is the <c>code</c> extension of a ProblemDetails.</summary>
public static class PaymentErrors
{
    public const string MissingIdempotencyKey = "missing_idempotency_key";
    public const string InvalidIdempotencyKey = "invalid_idempotency_key";
    public const string InvalidType = "invalid_type";
    public const string UnknownUser = "unknown_user";
    public const string UnknownRecipient = "unknown_recipient";
    public const string SameAccount = "same_account";
    public const string InvalidAmount = "invalid_amount";
    public const string NothingDue = "nothing_due";
    public const string BillsPaidInFull = "bills_paid_in_full";
    public const string UnknownProduct = "unknown_product";
    public const string InsufficientFunds = "insufficient_funds";
    public const string PinRequired = "pin_required";
    public const string PinInvalid = "pin_invalid";
    public const string PinLocked = "pin_locked";
    public const string InProgress = "in_progress";
    public const string UnknownKey = "unknown_key";
}

/// <summary>Why the wallet refused a request: the HTTP status, the code, and the extra figures some codes carry.</summary>
public sealed record PaymentRefusal(int StatusCode, string Code, long? AmountDueIqd = null, int? AttemptsLeft = null)
{
    public static PaymentRefusal Unprocessable(string code) => new(StatusCodes.Status422UnprocessableEntity, code);
}

/// <summary>A request that passed validation, with every figure taken from the wallet's own data.</summary>
public sealed record PaymentPlan(
    int UserId,
    int FromAccountId,
    long BalanceBeforeIqd,
    TransactionType Type,
    long AmountIqd,
    long FeeIqd,
    long TotalIqd,
    bool PinRequired,
    int? ToAccountId,
    string? RecipientPhone,
    string? RecipientName,
    int? BillAccountId,
    int? ProductId)
{
    public QuoteResponse ToQuote() => new(
        TransactionNames.Of(Type),
        AmountIqd,
        FeeIqd,
        TotalIqd,
        BalanceBeforeIqd,
        BalanceBeforeIqd - TotalIqd,
        BalanceBeforeIqd >= TotalIqd,
        PinRequired,
        RecipientPhone,
        RecipientName,
        BillAccountId,
        ProductId);
}

/// <summary>Either a plan or a refusal.</summary>
public sealed record PaymentCheck(PaymentPlan? Plan, PaymentRefusal? Refusal)
{
    [MemberNotNullWhen(true, nameof(Plan))]
    [MemberNotNullWhen(false, nameof(Refusal))]
    public bool IsValid => Plan is not null;
}

/// <summary>The lower-case names transaction types and statuses carry in JSON.</summary>
public static class TransactionNames
{
    public static string Of(TransactionType type) => type switch
    {
        TransactionType.Transfer => "transfer",
        TransactionType.Bill => "bill",
        TransactionType.Purchase => "purchase",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string Of(TransactionStatus status) => status switch
    {
        TransactionStatus.Executing => "executing",
        TransactionStatus.Done => "done",
        TransactionStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>Only the exact lower-case names are accepted.</summary>
    public static bool TryParse(string? name, out TransactionType type)
    {
        switch (name)
        {
            case "transfer": type = TransactionType.Transfer; return true;
            case "bill": type = TransactionType.Bill; return true;
            case "purchase": type = TransactionType.Purchase; return true;
            default: type = default; return false;
        }
    }
}

/// <summary>
/// Checks a payment request against the wallet's data and works out its figures. Shared by quote and execute,
/// so both refuse with the same codes. It never refuses on funds; only execute does, at the moment of the debit.
/// </summary>
public sealed class PaymentValidator(WalletDbContext db, FeeCalculator fees, IOptions<WalletOptions> options)
{
    public async Task<PaymentCheck> ValidateAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TransactionNames.TryParse(request.Type, out var type))
            return Refuse(PaymentErrors.InvalidType);

        // Every user has exactly one default account (D10); the payer pays from it.
        var payer = await db.Accounts.AsNoTracking()
            .Where(a => a.UserId == request.UserId && a.IsDefault)
            .Select(a => new { a.Id, a.BalanceIqd })
            .SingleOrDefaultAsync(cancellationToken);
        if (payer is null)
            return Refuse(PaymentErrors.UnknownUser);

        var draft = type switch
        {
            TransactionType.Transfer => await TransferAsync(request, payer.Id, cancellationToken),
            TransactionType.Bill => await BillAsync(request, cancellationToken),
            _ => await PurchaseAsync(request, cancellationToken),
        };
        if (draft.Refusal is not null)
            return new PaymentCheck(null, draft.Refusal);

        var fee = fees.Fee(type, draft.AmountIqd);
        if (draft.AmountIqd > long.MaxValue - fee)
            return Refuse(PaymentErrors.InvalidAmount);

        var total = draft.AmountIqd + fee;
        return new PaymentCheck(
            new PaymentPlan(
                request.UserId,
                payer.Id,
                payer.BalanceIqd,
                type,
                draft.AmountIqd,
                fee,
                total,
                total > options.Value.PinRequiredAboveIqd,
                draft.ToAccountId,
                draft.RecipientPhone,
                draft.RecipientName,
                draft.BillAccountId,
                draft.ProductId),
            null);
    }

    private async Task<Draft> TransferAsync(PaymentRequest request, int fromAccountId, CancellationToken cancellationToken)
    {
        if (request.AmountIqd is not > 0)
            return Draft.Refused(PaymentErrors.InvalidAmount);

        if (request.ToAccountId is null && string.IsNullOrEmpty(request.ToPhone))
            return Draft.Refused(PaymentErrors.UnknownRecipient);

        var recipients = db.Accounts.AsNoTracking();
        if (request.ToAccountId is { } accountId)
            recipients = recipients.Where(a => a.Id == accountId);

        // A phone number reaches its holder's default account.
        if (!string.IsNullOrEmpty(request.ToPhone))
            recipients = recipients.Where(a => a.IsDefault && a.User!.Phone == request.ToPhone);

        var recipient = await recipients
            .Select(a => new { a.Id, a.User!.Phone, a.User.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);
        if (recipient is null)
            return Draft.Refused(PaymentErrors.UnknownRecipient);

        if (recipient.Id == fromAccountId)
            return Draft.Refused(PaymentErrors.SameAccount);

        return new Draft(request.AmountIqd.Value, recipient.Id, recipient.Phone, recipient.DisplayName, null, null, null);
    }

    private async Task<Draft> BillAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        // A bill account that is missing or belongs to someone else is an unknown payee.
        var bill = request.BillAccountId is { } billAccountId
            ? await db.BillAccounts.AsNoTracking()
                .Where(b => b.Id == billAccountId && b.UserId == request.UserId)
                .Select(b => new { b.Id, b.AmountDueIqd })
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        if (bill is null)
            return Draft.Refused(PaymentErrors.UnknownRecipient);

        if (bill.AmountDueIqd <= 0)
            return Draft.Refused(PaymentErrors.NothingDue);

        // Bills are paid in full (D9): the amount comes from the biller, and any other amount is refused.
        if (request.AmountIqd is { } asked && asked != bill.AmountDueIqd)
            return new Draft(0, null, null, null, null, null,
                new PaymentRefusal(StatusCodes.Status422UnprocessableEntity, PaymentErrors.BillsPaidInFull, AmountDueIqd: bill.AmountDueIqd));

        return new Draft(bill.AmountDueIqd, null, null, null, bill.Id, null, null);
    }

    private async Task<Draft> PurchaseAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var product = request.ProductId is { } productId
            ? await db.Products.AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => new { p.Id, p.PriceIqd })
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        if (product is null)
            return Draft.Refused(PaymentErrors.UnknownProduct);

        // The price always comes from the catalogue (D11).
        if (request.AmountIqd is { } asked && asked != product.PriceIqd)
            return Draft.Refused(PaymentErrors.InvalidAmount);

        return new Draft(product.PriceIqd, null, null, null, null, product.Id, null);
    }

    private static PaymentCheck Refuse(string code) => new(null, PaymentRefusal.Unprocessable(code));

    private sealed record Draft(
        long AmountIqd,
        int? ToAccountId,
        string? RecipientPhone,
        string? RecipientName,
        int? BillAccountId,
        int? ProductId,
        PaymentRefusal? Refusal)
    {
        public static Draft Refused(string code) => new(0, null, null, null, null, null, PaymentRefusal.Unprocessable(code));
    }
}
