namespace Wallet.MockApi.Data;

/// <summary>What <see cref="SeedReset.ResetAsync"/> does with each user's consent after it restores the seed.</summary>
public enum ConsentReset
{
    /// <summary>Every user is left not consented (the default of <c>POST /admin/reset</c> and of <c>migrate</c>).</summary>
    Cleared,

    /// <summary>Each user keeps the seed's <c>consent_given</c> value (the test set's roles).</summary>
    Seed,
}
