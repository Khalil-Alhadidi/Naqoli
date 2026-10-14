namespace Wallet.MockApi.Data.Entities;

/// <summary>One executed or executing money movement: a transfer, a bill payment or a purchase.</summary>
public class Transaction
{
    public int Id { get; set; }

    /// <summary>The caller's idempotency key. Unique, so a retry finds the same row.</summary>
    public required string IdempotencyKey { get; set; }

    /// <summary>
    /// <c>ZC-</c> followed by 10 digits. Null only between inserting a runtime row and assigning its reference.
    /// </summary>
    public string? Reference { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public int FromAccountId { get; set; }

    public Account? FromAccount { get; set; }

    public TransactionType Type { get; set; }

    public TransactionStatus Status { get; set; }

    public string? ErrorCode { get; set; }

    public long AmountIqd { get; set; }

    public long FeeIqd { get; set; }

    public long TotalIqd { get; set; }

    public int? ToAccountId { get; set; }

    public Account? ToAccount { get; set; }

    public int? BillAccountId { get; set; }

    public BillAccount? BillAccount { get; set; }

    public int? ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>
    /// SHA-256, lower-case hex, of what the caller asked for under <see cref="IdempotencyKey"/> (see
    /// <c>Services/RequestFingerprint.cs</c>). A later call with the same key and a different fingerprint is refused,
    /// not answered with this row. Seeded rows hold the fingerprint of their own fields; their keys are never accepted.
    /// </summary>
    public string RequestFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// The payer's balance right after this payment (after the debit and any credit, or after a failure was stored),
    /// written in the same database transaction. Null for seeded rows, whose responses report the balance now.
    /// </summary>
    public long? BalanceAfterIqd { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

public enum TransactionType
{
    Transfer,
    Bill,
    Purchase,
}

public enum TransactionStatus
{
    Executing,
    Done,
    Failed,
}
