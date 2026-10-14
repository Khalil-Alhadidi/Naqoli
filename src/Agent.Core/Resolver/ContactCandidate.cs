using Agent.Core.Wallet;

namespace Agent.Core.Resolver;

/// <summary>
/// One name the resolver can match: a contact's saved name or an alias, with the account it points to. Closed accounts
/// stay candidates; the check step refuses them later.
/// </summary>
/// <param name="Text">The saved name or the alias text, as stored.</param>
/// <param name="IsAlias">True for an alias.</param>
/// <param name="ContactCode">The contact on this account (for an alias, the contact on the same account, if any).</param>
public sealed record ContactCandidate(
    string Text,
    bool IsAlias,
    int AccountId,
    string Phone,
    string? RegisteredName,
    string AccountStatus,
    string? ContactCode,
    DateTimeOffset? LastSentAt,
    long? LastSentAmountIqd)
{
    /// <summary>One candidate per saved name, in wallet order, then one per alias.</summary>
    public static IReadOnlyList<ContactCandidate> From(WalletContacts contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        var candidates = new List<ContactCandidate>(contacts.Contacts.Count + contacts.Aliases.Count);
        foreach (var contact in contacts.Contacts)
        {
            candidates.Add(new ContactCandidate(
                contact.SavedName, false, contact.TargetAccountId, contact.Phone, contact.RegisteredName,
                contact.AccountStatus, contact.Code, contact.LastSentAt, contact.LastSentAmountIqd));
        }

        foreach (var alias in contacts.Aliases)
        {
            var contact = contacts.Contacts.FirstOrDefault(c => c.TargetAccountId == alias.TargetAccountId);
            candidates.Add(new ContactCandidate(
                alias.Text, true, alias.TargetAccountId, alias.Phone, alias.RegisteredName, alias.AccountStatus,
                contact?.Code, contact?.LastSentAt, contact?.LastSentAmountIqd));
        }

        return candidates;
    }
}
