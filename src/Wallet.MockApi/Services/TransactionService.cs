using System.Data;
using System.Text.RegularExpressions;
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
/// The only code in the mock wallet that moves money. One idempotency key executes at most once: a known key replays
/// its stored outcome before anything else is checked, and the unique index on <see cref="Transaction.IdempotencyKey"/>
/// settles two calls with the same key that arrive together. A balance never goes below zero: the debit is one
/// conditional update. The fault switch applies here and only here, and every payment holds the
/// <see cref="WalletGate"/> shared, so a reset never runs in the middle of one.
/// </summary>
public sealed partial class TransactionService(
    WalletDbContext db,
    PaymentValidator validator,
    TimeProvider clock,
    IOptions<WalletOptions> options,
    FaultSwitch faults,
    WalletGate gate,
    ILogger<TransactionService> logger)
{
    /// <summary>The longest idempotency key accepted; the column is this wide.</summary>
    public const int MaxIdempotencyKeyLength = 64;

    private const int SqlDuplicateKeyRow = 2601;
    private const int SqlUniqueConstraint = 2627;

    // Generous: each failed try means another call changed the same user row in between.
    private const int MaxPinReserveTries = 50;

    /// <summary>The prefix of the seeded rows' keys; no caller may use a key that starts with it.</summary>
    public const string SeedKeyPrefix = "seed-";

    /// <summary>The prefix of the external debits' keys (<c>POST /admin/debit</c>); no caller may use a key that starts with it.</summary>
    public const string AdminKeyPrefix = "admin-";

    /// <summary>
    /// True when <paramref name="key"/> is 1 to <see cref="MaxIdempotencyKeyLength"/> characters of ASCII letters,
    /// digits, <c>_</c> and <c>-</c> (a GUID in "N" or "D" form fits), and does not start with
    /// <see cref="SeedKeyPrefix"/> or <see cref="AdminKeyPrefix"/> in any case (keys compare without case in the
    /// database): seeded rows and external debits can be neither replayed nor looked up by key.
    /// </summary>
    public static bool IsValidIdempotencyKey(string? key)
        => key is not null
            && IdempotencyKeyFormat().IsMatch(key)
            && !key.StartsWith(SeedKeyPrefix, StringComparison.OrdinalIgnoreCase)
            && !key.StartsWith(AdminKeyPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies the injected fault, if any; then, inside the gate, replays a known key, or validates, checks the PIN,
    /// records the key and moves the money in one database transaction.
    /// </summary>
    /// <remarks>
    /// Fault delays sit outside the gate, so a hanging request never holds up a reset. A timeout after execute
    /// waits only once the database transaction has committed; if the caller goes away during that wait, the
    /// committed result is returned as normal and nothing is thrown.
    /// </remarks>
    public async Task<ExecuteResult> ExecuteAsync(string idempotencyKey, PaymentRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidIdempotencyKey(idempotencyKey))
            throw new ArgumentException("The idempotency key is missing or malformed.", nameof(idempotencyKey));
        ArgumentNullException.ThrowIfNull(request);

        var fault = faults.Take();
        if (fault.Mode != FaultMode.None)
            LogFault(logger, fault.Mode, fault.DelayMs);

        switch (fault.Mode)
        {
            case FaultMode.Error:
                return new ExecuteResult.Refused(new PaymentRefusal(StatusCodes.Status500InternalServerError, FaultErrors.InjectedError));

            case FaultMode.Slow:
                await Task.Delay(TimeSpan.FromMilliseconds(fault.DelayMs), clock, cancellationToken);
                break;

            case FaultMode.TimeoutBeforeExecute:
                await Task.Delay(TimeSpan.FromMilliseconds(fault.DelayMs), clock, cancellationToken);
                return new ExecuteResult.Refused(new PaymentRefusal(StatusCodes.Status504GatewayTimeout, FaultErrors.InjectedTimeout));
        }

        ExecuteResult result;
        using (await gate.EnterSharedAsync(cancellationToken))
            result = await ExecuteCoreAsync(idempotencyKey, request, cancellationToken);

        if (fault.Mode == FaultMode.TimeoutAfterExecute)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(fault.DelayMs), clock, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller has gone; the outcome is committed and can be found by its key.
            }
        }

        return result;
    }

    private async Task<ExecuteResult> ExecuteCoreAsync(string idempotencyKey, PaymentRequest request, CancellationToken cancellationToken)
    {
        // What was asked, from the body as received; a known key answers only the same request.
        var fingerprint = RequestFingerprint.Of(request);

        // A known key is answered from its record at once: no validation, no PIN check (so no attempt is counted
        // and no user row changes), no insert. A retry never depends on data the first call changed.
        if (await LoadAsync(idempotencyKey, cancellationToken) is { } existing)
            return await ReplayAsync(existing, fingerprint, cancellationToken);

        // The key was unknown a moment ago; a call with the same key may still commit while this one is checked, so
        // a refusal looks the key up once more before it is returned.
        var check = await validator.ValidateAsync(request, cancellationToken);
        if (!check.IsValid)
            return await RefuseUnlessKnownAsync(idempotencyKey, fingerprint, check.Refusal, cancellationToken);

        var plan = check.Plan;
        if (request.Pin is not null || plan.PinRequired)
        {
            var pinRefusal = await CheckPinAsync(idempotencyKey, fingerprint, plan.UserId, request.Pin, cancellationToken);
            if (pinRefusal is not null)
                return pinRefusal;
        }

        return await RunAsync(idempotencyKey, fingerprint, plan, cancellationToken);
    }

    /// <summary>
    /// The transaction <paramref name="userId"/> recorded under a key; null when the key is unknown or belongs to
    /// another user, so a wrong user learns nothing. <see cref="TransactionResponse.BalanceAfterIqd"/> is the stored
    /// balance right after that payment.
    /// </summary>
    public async Task<TransactionResponse?> FindAsync(string idempotencyKey, int userId, CancellationToken cancellationToken)
    {
        var row = await db.Transactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey && t.UserId == userId, cancellationToken);
        return row is null ? null : await ToResponseAsync(row, cancellationToken);
    }

    /// <summary>
    /// Reserves an attempt before the slow PBKDF2 check, so concurrent guesses cannot all see the PIN unlocked: at most
    /// <see cref="WalletOptions.MaxPinAttempts"/> checks are counted before the lock, however many arrive together.
    /// A right PIN gives its attempt back. A PIN that is not 4 to 6 ASCII digits is refused before any attempt is
    /// reserved, so it counts no attempt and changes no user row.
    /// </summary>
    private async Task<ExecuteResult?> CheckPinAsync(
        string key, string fingerprint, int userId, string? pin, CancellationToken cancellationToken)
    {
        if (pin is null)
            return await RefuseUnlessKnownAsync(key, fingerprint, PaymentRefusal.Unprocessable(PaymentErrors.PinRequired), cancellationToken);

        if (!PinFormat().IsMatch(pin))
            return await RefuseUnlessKnownAsync(key, fingerprint, PaymentRefusal.Unprocessable(PaymentErrors.PinMalformed), cancellationToken);

        var maxAttempts = options.Value.MaxPinAttempts;
        var reservation = await ReservePinAttemptAsync(userId, maxAttempts, cancellationToken);
        if (reservation is null)
        {
            return await RefuseUnlessKnownAsync(
                key, fingerprint, new PaymentRefusal(StatusCodes.Status423Locked, PaymentErrors.PinLocked), cancellationToken);
        }

        if (PinHasher.Verify(pin, reservation.PinHash))
        {
            // Reset the count. Clear the lock only when this attempt set it (it held the last slot); a lock that
            // other wrong entries set after this reservation stands.
            var lockedByThisAttempt = reservation.Attempts >= maxAttempts;
            await db.Users
                .Where(u => u.Id == userId && (!u.PinLocked || lockedByThisAttempt))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PinFailedAttempts, 0)
                    .SetProperty(u => u.PinLocked, false),
                    cancellationToken);
            return null;
        }

        LogWrongPin(logger, userId, reservation.Attempts);
        return new ExecuteResult.Refused(new PaymentRefusal(
            StatusCodes.Status422UnprocessableEntity,
            PaymentErrors.PinInvalid,
            AttemptsLeft: Math.Max(0, maxAttempts - reservation.Attempts)));
    }

    /// <summary>
    /// Counts one attempt against the user, and locks the PIN when that attempt is the last allowed. The update is
    /// conditional on the PIN being unlocked and on the count read just before (compare and swap), so the attempt
    /// number it returns is exact under concurrency without a second read. Null when the PIN is locked.
    /// </summary>
    private async Task<PinReservation?> ReservePinAttemptAsync(int userId, int maxAttempts, CancellationToken cancellationToken)
    {
        for (var tries = 0; tries < MaxPinReserveTries; tries++)
        {
            var user = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.PinHash, u.PinFailedAttempts, u.PinLocked })
                .SingleAsync(cancellationToken);
            if (user.PinLocked)
                return null;

            var seen = user.PinFailedAttempts;
            var attempts = seen + 1;
            var lockNow = attempts >= maxAttempts;
            var reserved = await db.Users
                .Where(u => u.Id == userId && !u.PinLocked && u.PinFailedAttempts == seen)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PinFailedAttempts, attempts)
                    .SetProperty(u => u.PinLocked, lockNow),
                    cancellationToken);
            if (reserved == 1)
                return new PinReservation(user.PinHash, attempts);

            // Another call changed the count or the lock between the read and the update: read again.
        }

        throw new InvalidOperationException($"Could not reserve a PIN attempt for user {userId}.");
    }

    private async Task<ExecuteResult> RunAsync(string key, string fingerprint, PaymentPlan plan, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var row = new Transaction
        {
            IdempotencyKey = key,
            RequestFingerprint = fingerprint,
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
            return await ReplayAsync(existing, fingerprint, cancellationToken);
        }

        row.Reference = ReferenceGenerator.ForId(row.Id);
        await db.SaveChangesAsync(cancellationToken);

        // From the debit on, the work runs to its commit without the request's token: a caller who disconnects
        // cannot cancel the commit half-way. The outcome is stored either way and found by the key.
        var toCommit = CancellationToken.None;

        // The balance floor: the debit happens only if the balance covers the total at this moment.
        var debited = await db.Accounts
            .Where(a => a.Id == plan.FromAccountId && a.BalanceIqd >= plan.TotalIqd)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.BalanceIqd, a => a.BalanceIqd - plan.TotalIqd), toCommit);
        if (debited == 0)
            return await FailAsync(transaction, row, PaymentErrors.InsufficientFunds);

        string? code = null;
        switch (plan.Type)
        {
            case TransactionType.Transfer:
                await CreditAsync(plan.ToAccountId!.Value, plan.AmountIqd, toCommit);
                break;

            case TransactionType.Bill:
                // Only the amount that was quoted is cleared, so two payments of one bill cannot both succeed.
                var cleared = await db.BillAccounts
                    .Where(b => b.Id == plan.BillAccountId && b.AmountDueIqd == plan.AmountIqd)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.AmountDueIqd, 0L), toCommit);
                if (cleared == 0)
                {
                    await CreditAsync(plan.FromAccountId, plan.TotalIqd, toCommit);
                    return await FailAsync(transaction, row, PaymentErrors.NothingDue);
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

        // The balance after the debit and any credit, read inside the transaction and stored, so a replay or a
        // lookup later reports this payment's figure, not whatever the balance has become.
        row.Status = TransactionStatus.Done;
        row.CompletedAt = clock.GetUtcNow();
        row.BalanceAfterIqd = await BalanceAsync(plan.FromAccountId, toCommit);
        await db.SaveChangesAsync(toCommit);
        await transaction.CommitAsync(toCommit);

        LogExecuted(logger, row.Id, row.Type, row.Status);
        return new ExecuteResult.Executed(ToResponse(row, row.BalanceAfterIqd.Value, code));
    }

    private async Task CreditAsync(int accountId, long amountIqd, CancellationToken cancellationToken)
    {
        var credited = await db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.BalanceIqd, a => a.BalanceIqd + amountIqd), cancellationToken);
        if (credited != 1)
            throw new InvalidOperationException($"Account {accountId} could not be credited.");
    }

    /// <summary>
    /// Stores the failure against the key and commits it, so a retry gets the same answer. Runs after the debit was
    /// tried, so it takes no cancellation token (see <see cref="RunAsync"/>).
    /// </summary>
    private async Task<ExecuteResult> FailAsync(IDbContextTransaction transaction, Transaction row, string errorCode)
    {
        row.Status = TransactionStatus.Failed;
        row.ErrorCode = errorCode;
        row.CompletedAt = clock.GetUtcNow();
        row.BalanceAfterIqd = await BalanceAsync(row.FromAccountId, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        LogExecuted(logger, row.Id, row.Type, row.Status);
        return new ExecuteResult.Refused(StoredFailure(errorCode));
    }

    private async Task<ExecuteResult> RefuseUnlessKnownAsync(
        string key, string fingerprint, PaymentRefusal refusal, CancellationToken cancellationToken)
    {
        var existing = await LoadAsync(key, cancellationToken);
        return existing is null ? new ExecuteResult.Refused(refusal) : await ReplayAsync(existing, fingerprint, cancellationToken);
    }

    /// <summary>
    /// The stored outcome of a key: the original response, the original refusal, or "in progress". Only for the same
    /// request: a different <paramref name="fingerprint"/> (another user, type, amount, recipient, bill or product) is
    /// refused with 422 <c>idempotency_key_reused</c>, and nothing of the stored row is returned.
    /// </summary>
    private async Task<ExecuteResult> ReplayAsync(Transaction row, string fingerprint, CancellationToken cancellationToken)
    {
        if (!string.Equals(row.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            LogKeyReused(logger, row.Id);
            return new ExecuteResult.Refused(PaymentRefusal.Unprocessable(PaymentErrors.IdempotencyKeyReused));
        }

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

    /// <summary>
    /// The response for a stored row. The balance is the one stored right after the payment; only seeded rows, which
    /// have none, report the payer's balance now.
    /// </summary>
    private async Task<TransactionResponse> ToResponseAsync(Transaction row, CancellationToken cancellationToken)
    {
        var code = row.Type == TransactionType.Purchase
            ? await db.Orders.AsNoTracking()
                .Where(o => o.TransactionId == row.Id)
                .Select(o => o.Code)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var balanceAfter = row.BalanceAfterIqd ?? await BalanceAsync(row.FromAccountId, cancellationToken);
        return ToResponse(row, balanceAfter, code);
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
        row.CompletedAt ?? row.CreatedAt,
        row.Status == TransactionStatus.Failed ? row.ErrorCode : null);

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is SqlException { Number: SqlDuplicateKeyRow or SqlUniqueConstraint };

    // Keep the upper bound equal to MaxIdempotencyKeyLength. \z, not $, so a trailing newline never matches.
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex IdempotencyKeyFormat();

    // ASCII digits only: the agent turns Arabic-Indic digits into ASCII before it sends a PIN. \z, not $, as above.
    [GeneratedRegex(@"^[0-9]{4,6}\z")]
    private static partial Regex PinFormat();

    private sealed record PinReservation(string PinHash, int Attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transaction {TransactionId} ({Type}) ended {Status}")]
    private static partial void LogExecuted(ILogger logger, int transactionId, TransactionType type, TransactionStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency key reused; returning the stored outcome of transaction {TransactionId} ({Status})")]
    private static partial void LogReplayed(ILogger logger, int transactionId, TransactionStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency key of transaction {TransactionId} reused for a different request; refused")]
    private static partial void LogKeyReused(ILogger logger, int transactionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Injected fault {Mode} ({DelayMs} ms) applied to a payment")]
    private static partial void LogFault(ILogger logger, FaultMode mode, int delayMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wrong PIN for user {UserId}; failed attempts now {Attempts}")]
    private static partial void LogWrongPin(ILogger logger, int userId, int attempts);
}
