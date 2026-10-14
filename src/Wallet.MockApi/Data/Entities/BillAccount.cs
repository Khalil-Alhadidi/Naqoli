namespace Wallet.MockApi.Data.Entities;

/// <summary>A user's account with a biller and the amount currently due. Bills are paid in full (D9).</summary>
public class BillAccount
{
    public int Id { get; set; }

    /// <summary>The seed id (<c>BS1</c>).</summary>
    public required string Code { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public int BillerId { get; set; }

    public Biller? Biller { get; set; }

    /// <summary>The user's own label for the bill account, such as home or shop; null when there is none.</summary>
    public string? Label { get; set; }

    /// <summary>The subscriber number with the biller.</summary>
    public required string AccountNumber { get; set; }

    /// <summary>Zero means nothing is due.</summary>
    public long AmountDueIqd { get; set; }
}
