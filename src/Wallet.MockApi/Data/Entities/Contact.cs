namespace Wallet.MockApi.Data.Entities;

/// <summary>A name a user saved against the phone number of a wallet account.</summary>
public class Contact
{
    public int Id { get; set; }

    /// <summary>The seed id (<c>S01</c>).</summary>
    public required string Code { get; set; }

    public int OwnerUserId { get; set; }

    public User? Owner { get; set; }

    public required string SavedName { get; set; }

    /// <summary>The phone number of an <see cref="Account"/>.</summary>
    public required string Phone { get; set; }
}
