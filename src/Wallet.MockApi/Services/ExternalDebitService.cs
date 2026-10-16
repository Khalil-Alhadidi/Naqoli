using System.Data;
using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Contracts;
using Wallet.MockApi.Data;
using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Services;

/// <summary>What an external debit did: the stored row's response, or the refusal (nothing written).</summary>
public abstract record DebitResult
{
    private DebitResult()
    {
    }

    public sealed record Debited(TransactionResponse Response) : DebitResult;

    public sealed record Refused(PaymentRefusal Refusal) : DebitResult;
}

/// <summary>
/// <c>POST /admin/debit</c> (Khalil, 14 Oct 2026): money leaves a user's wallet from outside the agent, to show a balance
/// that changed between the card and the tap. Holds the <see cref="WalletGate"/> exclusive, so no payment and no reset
/// interleaves; refuses a closed default account (<c>account_closed</c>); debits the user's default account with one
/// conditional update that never goes below zero; and writes a
/// <c>done</c> <see cref="TransactionType.ExternalDebit"/> row, fee 0, so the history shows it. The fault switch does
/// not apply. The row's key is <c>admin-</c> and a GUID, which callers can neither replay nor look up.
/// </summary>
public sealed class ExternalDebitService(WalletDbContext db, TimeProvider clock, WalletGate gate)
{
    public async Task<DebitResult> DebitAsync(DebitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AmountIqd <= 0)
            return new DebitResult.Refused(PaymentRefusal.Unprocessable(PaymentErrors.InvalidAmount));

        using (await gate.EnterExclusiveAsync(cancellationToken))
        {
            // Every user has exactly one default account (D10); the debit is taken from it. A closed account gives
            // nothing, as it pays nothing on execute (Khalil, 16 Oct 2026): account_closed, nothing written.
            var account = await db.Accounts.AsNoTracking()
                .Where(a => a.UserId == request.UserId && a.IsDefault)
                .Select(a => new { a.Id, a.Status })
                .SingleOrDefaultAsync(cancellationToken);
            if (account is null)
                return new DebitResult.Refused(PaymentRefusal.Unprocessable(PaymentErrors.UnknownUser));
            if (account.Status == AccountStatus.Closed)
                return new DebitResult.Refused(PaymentRefusal.Unprocessable(PaymentErrors.AccountClosed));
            var fromAccountId = account.Id;

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            // The balance floor: one conditional update, so the balance never goes below zero.
            var debited = await db.Accounts
                .Where(a => a.Id == fromAccountId && a.BalanceIqd >= request.AmountIqd)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.BalanceIqd, a => a.BalanceIqd - request.AmountIqd), cancellationToken);
            if (debited == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DebitResult.Refused(PaymentRefusal.Unprocessable(PaymentErrors.InsufficientFunds));
            }

            var now = clock.GetUtcNow();
            var row = new Transaction
            {
                IdempotencyKey = TransactionService.AdminKeyPrefix + Guid.NewGuid().ToString("N"),
                RequestFingerprint = RequestFingerprint.Of(new PaymentRequest(
                    request.UserId, TransactionNames.Of(TransactionType.ExternalDebit), request.AmountIqd, null, null, null, null, null)),
                UserId = request.UserId,
                FromAccountId = fromAccountId,
                Type = TransactionType.ExternalDebit,
                Status = TransactionStatus.Done,
                AmountIqd = request.AmountIqd,
                FeeIqd = 0,
                TotalIqd = request.AmountIqd,
                CreatedAt = now,
                CompletedAt = now,
            };
            db.Transactions.Add(row);
            await db.SaveChangesAsync(cancellationToken);

            row.Reference = ReferenceGenerator.ForId(row.Id);
            row.BalanceAfterIqd = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == fromAccountId)
                .Select(a => a.BalanceIqd)
                .SingleAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new DebitResult.Debited(new TransactionResponse(
                row.Id,
                row.Reference,
                TransactionNames.Of(row.Type),
                TransactionNames.Of(row.Status),
                row.AmountIqd,
                row.FeeIqd,
                row.TotalIqd,
                row.BalanceAfterIqd.Value,
                Code: null,
                CompletedAt: now,
                ErrorCode: null));
        }
    }
}
