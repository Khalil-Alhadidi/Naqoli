namespace Wallet.MockApi.Contracts;

/// <summary>A wallet holder: id, test-set code, phone, the registered name, and whether they have given consent.</summary>
public sealed record UserSummary(int Id, string Code, string Phone, string RegisteredName, bool ConsentGiven);
