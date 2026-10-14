namespace Agent.Core.Pending;

/// <summary>
/// One confirmation card and the payment behind it, built by code from the wallet's quote (rule 3). Only the Confirm
/// handler, given its <see cref="Id"/>, may execute it, under <see cref="IdempotencyKey"/>. It holds no PIN and only the
/// last four digits of a purchase code.
/// </summary>
public sealed class PendingRecord
{
    public Guid Id { get; set; }

    public int UserId { get; set; }

    public PendingState State { get; set; }

    /// <summary>A lower-case GUID without dashes (32 characters), unique; every execute of this record carries it.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary><c>transfer</c>, <c>bill</c> or <c>purchase</c>.</summary>
    public string Type { get; set; } = string.Empty;

    public long AmountIqd { get; set; }

    public long FeeIqd { get; set; }

    public long TotalIqd { get; set; }

    /// <summary>The balance the quote reported; for the card's balance after, never for the model (rule 5).</summary>
    public long QuotedBalanceIqd { get; set; }

    public string? ToPhone { get; set; }

    public int? ToAccountId { get; set; }

    public string? DisplayName { get; set; }

    public string? RegisteredName { get; set; }

    public int? BillAccountId { get; set; }

    public string? BillCode { get; set; }

    public int? ProductId { get; set; }

    public string? ProductCode { get; set; }

    public bool PinRequired { get; set; }

    public bool FirstTime { get; set; }

    /// <summary>The resource key of a warning shown on the card, if any.</summary>
    public string? Warning { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// When the record last entered Executing: set by <see cref="PendingStore.TryBeginExecuteAsync"/>, cleared when it
    /// goes back to Pending. The stale-Executing sweep (<see cref="PendingStore.MarkStaleExecutingAsync"/>) reads it.
    /// </summary>
    public DateTimeOffset? ExecutingSince { get; set; }

    public string? Reference { get; set; }

    public long? BalanceAfterIqd { get; set; }

    /// <summary>The wallet's refusal code, or the reason the outcome is unknown.</summary>
    public string? ResultCode { get; set; }

    public string? PurchaseCodeLast4 { get; set; }

    /// <summary>How many execute calls this record has made.</summary>
    public int Attempts { get; set; }

    /// <summary>The SQL Server rowversion; set by the database, so it is null until the record is first saved.</summary>
    public byte[] RowVersion { get; set; } = null!;

    /// <summary>
    /// The transitions of architecture section 5: Pending to Cancelled, Expired or Executing; Executing to Done, NotDone,
    /// Checking or back to Pending (a wrong PIN keeps the card); Checking to Done or NotDone. Nothing else.
    /// </summary>
    public static bool CanLeave(PendingState from, PendingState to) => (from, to) switch
    {
        (PendingState.Pending, PendingState.Cancelled or PendingState.Expired or PendingState.Executing) => true,
        (PendingState.Executing, PendingState.Done or PendingState.NotDone or PendingState.Checking or PendingState.Pending) => true,
        (PendingState.Checking, PendingState.Done or PendingState.NotDone) => true,
        _ => false,
    };
}
