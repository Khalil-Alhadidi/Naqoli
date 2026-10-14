namespace Wallet.MockApi.Data.Entities;

/// <summary>A user's account with a biller and the amount currently due. Bills are paid in full (D9).</summary>
public class BillAccount
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public int BillerId { get; set; }

    public Biller? Biller { get; set; }

    /// <summary>The user's own label for the bill account, such as home or shop.</summary>
    public required string Label { get; set; }

    public required string AccountNumber { get; set; }

    /// <summary>Zero means nothing is due.</summary>
    public long AmountDueIqd { get; set; }

    public DateOnly? DueDate { get; set; }
}
