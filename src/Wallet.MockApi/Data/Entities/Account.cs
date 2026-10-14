namespace Wallet.MockApi.Data.Entities;

/// <summary>A wallet account. Balances are whole Iraqi dinars.</summary>
public class Account
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public bool IsDefault { get; set; }

    public long BalanceIqd { get; set; }
}
