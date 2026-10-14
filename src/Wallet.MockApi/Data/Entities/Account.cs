namespace Wallet.MockApi.Data.Entities;

/// <summary>
/// A wallet account, found by its phone number. Balances are whole Iraqi dinars. A user's own account has a
/// <see cref="UserId"/> and <see cref="IsDefault"/> set; the accounts behind contacts and other numbers have no user.
/// </summary>
public class Account
{
    public int Id { get; set; }

    /// <summary>The seed id of an account of kind <c>other</c> (<c>X01</c>); null for every other account.</summary>
    public string? Code { get; set; }

    public required string Phone { get; set; }

    /// <summary>The registered name, in Arabic; null when the wallet holds none for the number.</summary>
    public string? RegisteredName { get; set; }

    public AccountStatus Status { get; set; }

    public int? UserId { get; set; }

    public User? User { get; set; }

    public bool IsDefault { get; set; }

    public long BalanceIqd { get; set; }
}

public enum AccountStatus
{
    Active,
    Closed,
}
