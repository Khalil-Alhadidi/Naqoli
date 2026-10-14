namespace Wallet.MockApi.Data.Entities;

/// <summary>A name a user saved against the phone number of another wallet holder.</summary>
public class Contact
{
    public int Id { get; set; }

    public int OwnerUserId { get; set; }

    public User? Owner { get; set; }

    public required string SavedName { get; set; }

    /// <summary>The phone number of another <see cref="User"/>.</summary>
    public required string Phone { get; set; }
}
