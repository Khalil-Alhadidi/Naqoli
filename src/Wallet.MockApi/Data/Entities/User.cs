namespace Wallet.MockApi.Data.Entities;

/// <summary>A wallet holder in the mock wallet. All seeded users are synthetic.</summary>
public class User
{
    public int Id { get; set; }

    public required string Phone { get; set; }

    public required string DisplayName { get; set; }

    public List<Account> Accounts { get; set; } = new();
}
