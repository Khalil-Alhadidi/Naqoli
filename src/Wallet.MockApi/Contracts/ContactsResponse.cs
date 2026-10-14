namespace Wallet.MockApi.Contracts;

/// <summary>The contacts and aliases of a user, optionally filtered by a search text.</summary>
public sealed record ContactsResponse(IReadOnlyList<ContactItem> Contacts, IReadOnlyList<AliasItem> Aliases);
