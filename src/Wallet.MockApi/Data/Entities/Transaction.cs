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
