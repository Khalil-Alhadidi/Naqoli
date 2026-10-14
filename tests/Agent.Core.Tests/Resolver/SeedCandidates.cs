using Agent.Core.Resolver;
using Agent.Core.Tests.Wallet;

namespace Agent.Core.Tests.Resolver;

/// <summary>
/// Builds a user's resolver candidates straight from the seed files (no wallet, no database), in the order
/// <see cref="ContactCandidate.From"/> uses: saved names in seed order, then aliases.
/// </summary>
internal static class SeedCandidates
{
    public static IReadOnlyList<ContactCandidate> For(string userCode)
    {
        var seed = SeedFacts.Seed;
        var contacts = seed.Contacts.Where(c => c.User == userCode).ToList();

        // A synthetic stable account id: the account's index in accounts.json.
        var accounts = seed.Accounts.ToList();
        int AccountIdOf(string phone) => accounts.FindIndex(a => a.Number == phone) + 1;

        ContactCandidate Build(string text, bool isAlias, global::Wallet.MockApi.Data.Seed.SeedContact contact)
        {
            var account = seed.Accounts.Single(a => a.Number == contact.Number);
            return new ContactCandidate(
                text, isAlias, AccountIdOf(contact.Number), contact.Number, account.RegisteredName, account.Status,
                contact.Id, LastSentAt: null, LastSentAmountIqd: null);
        }

        return
        [
            .. contacts.Select(c => Build(c.SavedName, isAlias: false, c)),
            .. contacts.Where(c => c.Alias is not null).Select(c => Build(c.Alias!, isAlias: true, c)),
        ];
    }
}
