namespace Wallet.MockApi.Data.Entities;

/// <summary>A distinct word a user chose for one account (D15). One alias points to one account.</summary>
public class Alias
{
    public int Id { get; set; }

    public int OwnerUserId { get; set; }

    public User? Owner { get; set; }

    public required string Text { get; set; }

    public int TargetAccountId { get; set; }

    public Account? TargetAccount { get; set; }
}
