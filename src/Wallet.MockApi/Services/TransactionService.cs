using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Services;

/// <summary>What became of an execute call.</summary>
public abstract record ExecuteResult
{
    /// <summary>The payment ran now (201).</summary>
    public sealed record Executed(TransactionResponse Response) : ExecuteResult;

    /// <summary>The key was used before and the payment had completed; its stored result (200).</summary>
    public sealed record Replayed(TransactionResponse Response) : ExecuteResult;

    /// <summary>Refused, now or (for a stored failure) when the key was first used.</summary>
    public sealed record Refused(PaymentRefusal Refusal) : ExecuteResult;
}

/// <summary>
/// The only code in the mock wallet that moves money. One idempotency key executes at most once: the unique index
/// on <see cref="Transaction.IdempotencyKey"/> is the guarantee, and a violation of it returns the stored outcome.
/// A balance never goes below zero: the debit is one conditional update.
/// </summary>
public sealed partial class TransactionService(
    WalletDbContext db,
    PaymentValidator validator,
    TimeProvider clock,
    IOptions<WalletOptions> options,
    ILogger<TransactionService> logger)
{
    /// <summary>The longest idempotency key accepted; the column is this wide.</summary>
    public const int MaxIdempotencyKeyLength = 64;

    private const int SqlDuplicateKeyRow = 2601;
    private const int SqlUniqueConstraint = 2627;

    /// <summary>
    /// Validates, checks the PIN, records the key, then moves the money in one database transaction.
    /// A request refused by validation or the PIN rules still returns the stored outcome when its key was used before,
    /// so a retry never depends on the body or on data the first call changed.
    /// </summary>
    public async Task<ExecuteResult> ExecuteAsync(string idempotencyKey, PaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        ArgumentNullException.ThrowIfNull(request);

        var check = await validator.ValidateAsync(request, cancellationToken);
        if (!check.IsValid)
            return await RefuseUnlessKnownAsync(idempotencyKey, check.Refusal, cancellationToken);

        var plan = check.Plan;
        if (request.Pin is not null || plan.PinRequired)
        {
            var pinRefusal = await CheckPinAsync(idempotencyKey, plan.UserId, request.Pin, cancellationToken);
            if (pinRefusal is not null)
                return pinRefusal;
        }

        return await RunAsync(idempotencyKey, plan, cancellationToken);
    }

    /// <summary>The transaction recorded under a key, with the payer's current balance; null when the key is unknown.</summary>
    public async Task<TransactionResponse?> FindAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(idempotencyKey, cancellationToken);
        return row is null ? null : await ToResponseAsync(row, cancellationToken);
    }

    private async Task<ExecuteResult?> CheckPinAsync(string key, int userId, string? pin, CancellationToken cancellationToken)
    {
        if (pin is null)
            return await RefuseUnlessKnownAsync(key, PaymentRefusal.Unprocessable(PaymentErrors.PinRequired), cancellationToken);

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.PinHash, u.PinLocked })
            .SingleAsync(cancellationToken);

        var locked = new PaymentRefusal(StatusCodes.Status423Locked, PaymentErrors.PinLocked);
        if (user.PinLocked)
            return await RefuseUnlessKnownAsync(key, locked, cancellationToken);

        if (PinHasher.Verify(pin, user.PinHash))
        {
            await db.Users
                .Where(u => u.Id == userId && u.PinFailedAttempts > 0 && !u.PinLocked)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PinFailedAttempts, 0), cancellationToken);
            return null;
        }

        // A retry of a key that already ran is answered from the record, and does not cost an attempt.
        if (await LoadAsync(key, cancellationToken) is { } existing)
            return await ReplayAsync(existing, cancellationToken);

        var maxAttempts = options.Value.MaxPinAttempts;
        var counted = await db.Users
            .Where(u => u.Id == userId && !u.PinLocked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.PinFailedAttempts, u => u.PinFailedAttempts + 1)
                .SetProperty(u => u.PinLocked, u => u.PinFailedAttempts + 1 >= maxAttempts),
                cancellationToken);
        if (counted == 0)
            return new ExecuteResult.Refused(locked);

        var attempts = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.PinFailedAttempts)
            .SingleAsync(cancellationToken);
        LogWrongPin(logger, userId, attempts);

        return new ExecuteResult.Refused(new PaymentRefusal(
            StatusCodes.Status422UnprocessableEntity, PaymentErrors.PinInvalid, AttemptsLeft: Math.Max(0, maxAttempts - attempts)));
    }

    private async Task<ExecuteResult> RunAsync(string key, PaymentPlan plan, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var row = new Transaction
        {
            IdempotencyKey = key,
            UserId = plan.UserId,
            FromAccountId = plan.FromAccountId,
            Type = plan.Type,
            Status = TransactionStatus.Executing,
            AmountIqd = plan.AmountIqd,
            FeeIqd = plan.FeeIqd,
            TotalIqd = plan.TotalIqd,
            ToAccountId = plan.ToAccountId,
            BillAccountId = plan.BillAccountId,
            ProductId = plan.ProductId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Transactions.Add(row);

        // Its own save, so a second use of the key fails here, before any money moves. A concurrent call with the
        // same key waits on the index until this transaction ends, then sees the violation and the stored outcome.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var existing = await LoadAsync(key, cancellationToken)
                ?? throw new InvalidOperationException("The idempotency key was reported as taken, but no transaction holds it.");
            return await ReplayAsync(existing, cancellationToken);
        }

        row.Reference = ReferenceGenerator.ForId(row.Id);
        await db.SaveChangesAsync(cancellationToken);

        // The balance floor: the debit happens only if the balance covers the total at this moment.
        var debited = await db.Accounts
            .Where(a => a.Id == plan.FromAccountId && a.BalanceIqd >= plan.TotalIqd)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.BalanceIqd, a => a.BalanceIqd - plan.TotalIqd), cancellationToken);
        if (debited == 0)
            return await FailAsync(transaction, row, PaymentErrors.InsufficientFunds, cancellationToken);

        string? code = null;
        switch (plan.Type)
        {
            case TransactionType.Transfer:
                await CreditAsync(plan.ToAccountId!.Value, plan.AmountIqd, cancellationToken);
                break;

            case TransactionType.Bill:
                // Only the amount that was quoted is cleared, so two payments of one bill cannot both succeed.
                var cleared = await db.BillAccounts
                    .Where(b => b.Id == plan.BillAccountId && b.AmountDueIqd == plan.AmountIqd)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.AmountDueIqd, 0L), cancellationToken);
                if (cleared == 0)
                {
                    await CreditAsync(plan.FromAccountId, plan.TotalIqd, cancellationToken);
                    return await FailAsync(transaction, row, PaymentErrors.NothingDue, cancellationToken);
                }

                break;

            case TransactionType.Purchase:
                code = VoucherCodeGenerator.Next();
                db.Orders.Add(new Order
                {
                    UserId = plan.UserId,
                    ProductId = plan.ProductId!.Value,
                    TransactionId = row.Id,
                    Code = code,
                    CreatedAt = clock.GetUtcNow(),
                });
                break;
        }

        row.Status = TransactionStatus.Done;
        row.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        var balanceAfter = await BalanceAsync(plan.FromAccountId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogExecuted(logger, row.Id, row.Type, row.Status);
        return new ExecuteResult.Executed(ToResponse(row, balanceAfter, code));
    }

    private async Task CreditAsync(int accountId, long amountIqd, CancellationToken cancellationToken)
    {
        var credited = await db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.BalanceIqd, a => a.BalanceIqd + amountIqd), cancellationToken);
        if (credited != 1)
            throw new InvalidOperationException($"Account {accountId} could not be credited.");
    }

    /// <summary>Stores the failure against the key and commits it, so a retry gets the same answer.</summary>
    private async Task<ExecuteResult> FailAsync(
        IDbContextTransaction transaction, Transaction row, string errorCode, CancellationToken cancellationToken)
    {
        row.Status = TransactionStatus.Failed;
        row.ErrorCode = errorCode;
        row.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogExecuted(logger, row.Id, row.Type, row.Status);
        return new ExecuteResult.Refused(StoredFailure(errorCode));
    }

    private async Task<ExecuteResult> RefuseUnlessKnownAsync(string key, PaymentRefusal refusal, CancellationToken cancellationToken)
    {
        var existing = await LoadAsync(key, cancellationToken);
        return existing is null ? new ExecuteResult.Refused(refusal) : await ReplayAsync(existing, cancellationToken);
    }

    /// <summary>The stored outcome of a key: the original response, the original refusal, or "in progress".</summary>
    private async Task<ExecuteResult> ReplayAsync(Transaction row, CancellationToken cancellationToken)
    {
        LogReplayed(logger, row.Id, row.Status);
        return row.Status switch
        {
            TransactionStatus.Done => new ExecuteResult.Replayed(await ToResponseAsync(row, cancellationToken)),
            TransactionStatus.Failed => new ExecuteResult.Refused(StoredFailure(row.ErrorCode)),
            _ => new ExecuteResult.Refused(new PaymentRefusal(StatusCodes.Status409Conflict, PaymentErrors.InProgress)),
        };
    }

    /// <summary>Every stored failure code is a 422, as it was when first returned.</summary>
    private static PaymentRefusal StoredFailure(string? errorCode)
        => PaymentRefusal.Unprocessable(errorCode ?? PaymentErrors.InsufficientFunds);

    private Task<Transaction?> LoadAsync(string key, CancellationToken cancellationToken)
        => db.Transactions.AsNoTracking().SingleOrDefaultAsync(t => t.IdempotencyKey == key, cancellationToken);

    private Task<long> BalanceAsync(int accountId, CancellationToken cancellationToken)
        => db.Accounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.BalanceIqd).SingleAsync(cancellationToken);

    private async Task<TransactionResponse> ToResponseAsync(Transaction row, CancellationToken cancellationToken)
    {
        var code = row.Type == TransactionType.Purchase
            ? await db.Orders.AsNoTracking()
                .Where(o => o.TransactionId == row.Id)
                .Select(o => o.Code)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        return ToResponse(row, await BalanceAsync(row.FromAccountId, cancellationToken), code);
    }

    private static TransactionResponse ToResponse(Transaction row, long balanceAfterIqd, string? code) => new(
        row.Id,
        row.Reference ?? string.Empty,
        TransactionNames.Of(row.Type),
        TransactionNames.Of(row.Status),
        row.AmountIqd,
        row.FeeIqd,
        row.TotalIqd,
        balanceAfterIqd,
        code,
        row.CompletedAt ?? row.CreatedAt);

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is SqlException { Number: SqlDuplicateKeyRow or SqlUniqueConstraint };

    [LoggerMessage(Level = LogLevel.Information, Message = "Transaction {TransactionId} ({Type}) ended {Status}")]
    private static partial void LogExecuted(ILogger logger, int transactionId, TransactionType type, TransactionStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency key reused; returning the stored outcome of transaction {TransactionId} ({Status})")]
    private static partial void LogReplayed(ILogger logger, int transactionId, TransactionStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wrong PIN for user {UserId}; failed attempts now {Attempts}")]
    private static partial void LogWrongPin(ILogger logger, int userId, int attempts);
}
