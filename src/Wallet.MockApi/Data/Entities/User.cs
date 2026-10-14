namespace Wallet.MockApi.Data.Entities;

/// <summary>A wallet holder in the mock wallet. All seeded users are synthetic.</summary>
public class User
{
    public int Id { get; set; }

    public required string Phone { get; set; }

    /// <summary>The registered name, in Arabic.</summary>
    public required string DisplayName { get; set; }

    /// <summary>The PIN as a salted PBKDF2 hash. The PIN itself is never stored.</summary>
    public required string PinHash { get; set; }

    public int PinFailedAttempts { get; set; }

    public bool PinLocked { get; set; }

    public List<Account> Accounts { get; set; } = new();

    public List<Contact> Contacts { get; set; } = new();

    public List<Alias> Aliases { get; set; } = new();

    public List<BillAccount> BillAccounts { get; set; } = new();
}
