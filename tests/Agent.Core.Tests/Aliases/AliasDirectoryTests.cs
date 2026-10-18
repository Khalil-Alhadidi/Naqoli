using System.Globalization;
using Agent.Core.Aliases;
using Agent.Core.Audit;
using Agent.Core.Data;
using Agent.Core.Pipeline;
using Agent.Core.Resolver;
using Agent.Core.Tests.Agent;
using Agent.Core.Tests.Conversation;
using Agent.Core.Tests.Wallet;
using Agent.Core.Trace;
using Agent.Core.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agent.Core.Tests.Aliases;

/// <summary>
/// The alias directory (milestone 9a, D15, ruling 4) against the host's in-process wallet: one alias, one account; a
/// text whose key equals any saved name or alias of the user is refused before the wallet is written. Every test starts
/// from a reset. Alias text comes from the seed (<see cref="SeedFacts"/>), never a literal.
/// </summary>
[Collection(AgentCollection.Name)]
public sealed class AliasDirectoryTests(AgentHost host) : IAsyncLifetime
{
    private const int U1 = SeedFacts.DemoUserId;

    /// <summary>A well-formed number with no wallet account behind it.</summary>
    private const string NoWalletPhone = "07805559999";

    /// <summary>The Arabic fatha (U+064E), a diacritic the normalisation drops.</summary>
    private const char Fatha = 'َ';

    private readonly ConversationHarness.RecordingTrace trace = new();
    private ServiceProvider? provider;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (!host.DatabaseAvailable)
            return;

        await host.ResetAsync();

        // The recording sink is registered first, so AddAgentCore's TryAddScoped keeps it.
        provider = host.BuildProvider((services, configuration) =>
        {
            services.AddSingleton<ITraceSink>(trace);
            services.AddAgentCore(configuration);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (provider is not null)
            await provider.DisposeAsync();
    }

    [Fact]
    public async Task SavesAnAliasForAContactAndListsIt()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var s03 = Contact(before, "S03");
        var text = s03.SavedName + " x";

        var saved = Assert.IsType<AliasResult.Saved>(await SaveAsync(text, s03.TargetAccountId));

        Assert.Equal(text, saved.Alias.Text);
        Assert.Equal(s03.TargetAccountId, saved.Alias.TargetAccountId);
        var after = await ListAsync();
        Assert.Equal(before.Aliases.Count + 1, after.Aliases.Count);
        Assert.Equal(saved.Alias, after.Aliases[^1]);

        var row = Assert.Single(await AuditAsync());
        Assert.Equal(AuditEvents.AliasSaved, row.Event);
        Assert.Equal(U1, row.UserId);
        Assert.Equal(s03.TargetAccountId.ToString(CultureInfo.InvariantCulture), row.Detail);
        Assert.DoesNotContain(row.Detail!, text, StringComparison.Ordinal);
        Assert.Null(row.Reason);

        var step = Assert.Single(trace.Entries, e => e.Step == TraceSteps.AliasSaved);
        Assert.Equal(TraceZone.Code, step.Zone);
        Assert.Equal(row.Detail, step.Detail);
    }

    [Fact]
    public async Task ATextThatMatchesAnotherContactsSavedNameIsRefusedAliasExists()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();

        var refused = Assert.IsType<AliasResult.Refused>(
            await SaveAsync(SeedFacts.Contact("S04").SavedName, Contact(before, "S03").TargetAccountId));

        Assert.Equal(ReasonCodes.AliasExists, refused.Code);
        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
        Assert.Empty(await AuditAsync());
        Assert.Contains(trace.Entries, e => e.Step == TraceSteps.AliasRefusedPrefix + ReasonCodes.AliasExists);
    }

    [Fact]
    public async Task ATextThatMatchesAnExistingAliasAfterNormalisingIsRefusedAliasExists()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var alias = SeedFacts.Contact("S01").Alias!;
        var marked = alias[..1] + Fatha + alias[1..];
        Assert.NotEqual(alias, marked);

        var refused = Assert.IsType<AliasResult.Refused>(await SaveAsync(marked, Contact(before, "S03").TargetAccountId));

        Assert.Equal(ReasonCodes.AliasExists, refused.Code);
        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
    }

    [Fact]
    public async Task ABareFirstNameSharedBySeveralSavedNamesIsRefusedAliasShared()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var s04 = Contact(before, "S04");
        var bare = SeedFacts.Contact("S04").SavedName.Split(' ')[0];

        // The bare word is a question on several accounts (S04, S05 and S06 share it), from the partial step (rule 4).
        var asked = Assert.IsType<Resolution.Options>(Resolve(bare, before));
        Assert.Equal(MatchStep.Partial, asked.Step);
        Assert.True(asked.Candidates.Count > 1);

        var refused = Assert.IsType<AliasResult.Refused>(await SaveAsync(bare, s04.TargetAccountId));
        Assert.Equal(ReasonCodes.AliasShared, refused.Code);

        var byPhone = Assert.IsType<AliasResult.Refused>(await SaveForPhoneAsync(bare, s04.Phone));
        Assert.Equal(ReasonCodes.AliasShared, byPhone.Code);

        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
        Assert.Empty(await AuditAsync());
        Assert.Equal(2, trace.Entries.Count(e => e.Step == TraceSteps.AliasRefusedPrefix + ReasonCodes.AliasShared));
        Assert.DoesNotContain(trace.Entries, e => e.Step == TraceSteps.AliasSaved);
    }

    [Fact]
    public async Task TwoWordsThatShareOneWordWithSeveralSavedNamesAreSaved()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var s04 = Contact(before, "S04");
        var s04Words = SeedFacts.Contact("S04").SavedName.Split(' ');

        // S04's bare first word, then the last word of another contact's saved name that holds neither word of S04's
        // name: two words that no saved name holds together, so the resolver asks on the shared word (rule 15).
        var text = before.Contacts
            .Where(c => c.Code != "S04")
            .Select(c => c.SavedName.Split(' '))
            .Where(words => words.Length > 1 && !words.Intersect(s04Words, StringComparer.Ordinal).Any())
            .Select(words => s04Words[0] + " " + words[^1])
            .FirstOrDefault(t => Resolve(t, before) is Resolution.Options { Step: MatchStep.SharedWord, Candidates.Count: > 1 });
        Assert.NotNull(text);

        var saved = Assert.IsType<AliasResult.Saved>(await SaveAsync(text, s04.TargetAccountId));

        Assert.Equal(text, saved.Alias.Text);
        Assert.Equal(s04.TargetAccountId, saved.Alias.TargetAccountId);
        Assert.DoesNotContain(trace.Entries, e => e.Step == TraceSteps.AliasRefusedPrefix + ReasonCodes.AliasShared);
    }

    [Fact]
    public async Task ABlankATooLongAndANumberLikeTextAreRefusedAliasInvalid()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var target = Contact(before, "S03").TargetAccountId;
        var maxLength = new AgentOptions().MaxAliasLength;
        Assert.Equal(100, maxLength);

        foreach (var text in new[] { "   ", new string('x', maxLength + 1), SeedFacts.HammoudiPhone })
        {
            var refused = Assert.IsType<AliasResult.Refused>(await SaveAsync(text, target));
            Assert.Equal(ReasonCodes.AliasInvalid, refused.Code);

            // The phone path checks the text before any wallet read (security review M3): even with the user's own
            // number or a number with no wallet, the text's refusal comes first.
            foreach (var phone in new[] { SeedFacts.DemoPhone, NoWalletPhone })
            {
                var byPhone = Assert.IsType<AliasResult.Refused>(await SaveForPhoneAsync(text, phone));
                Assert.Equal(ReasonCodes.AliasInvalid, byPhone.Code);
            }
        }

        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
        Assert.Empty(await AuditAsync());
    }

    [Fact]
    public async Task ATargetThatIsNotAContactIsRefusedRecipientNotFound()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var stranger = await NewPersonAsync(before);

        var refused = Assert.IsType<AliasResult.Refused>(
            await SaveAsync(Contact(before, "S03").SavedName + " x", stranger.AccountId));

        Assert.Equal(ReasonCodes.RecipientNotFound, refused.Code);
        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
    }

    [Fact]
    public async Task AnAliasForAContactWhoseAccountIsClosedIsRefusedAccountClosed()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var s20 = Contact(before, "S20");
        Assert.Equal(Checks.ClosedStatus, s20.AccountStatus, ignoreCase: true);

        var refused = Assert.IsType<AliasResult.Refused>(await SaveAsync(s20.SavedName + " x", s20.TargetAccountId));

        Assert.Equal(ReasonCodes.AccountClosed, refused.Code);
        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
        Assert.Empty(await AuditAsync());
        Assert.Contains(trace.Entries, e => e.Step == TraceSteps.AliasRefusedPrefix + ReasonCodes.AccountClosed);
        Assert.DoesNotContain(trace.Entries, e => e.Step == TraceSteps.AliasSaved);
    }

    [Fact]
    public async Task AnAliasForTheTypedNumberOfAClosedAccountIsRefusedAccountClosed()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var s20 = Contact(before, "S20");

        var refused = Assert.IsType<AliasResult.Refused>(await SaveForPhoneAsync(s20.SavedName + " x", s20.Phone));

        Assert.Equal(ReasonCodes.AccountClosed, refused.Code);
        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
        Assert.Empty(await AuditAsync());
        Assert.Contains(trace.Entries, e => e.Step == TraceSteps.AliasRefusedPrefix + ReasonCodes.AccountClosed);
    }

    [Fact]
    public async Task AnAliasForANumberWithNoWalletIsRefused()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();

        var refused = Assert.IsType<AliasResult.Refused>(
            await SaveForPhoneAsync(Contact(before, "S03").SavedName + " x", NoWalletPhone));

        Assert.Equal(ReasonCodes.NumberHasNoWallet, refused.Code);
        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
    }

    [Fact]
    public async Task AnAliasForTheUsersOwnNumberIsRefused()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();

        var refused = Assert.IsType<AliasResult.Refused>(
            await SaveForPhoneAsync(Contact(before, "S03").SavedName + " x", SeedFacts.DemoPhone));

        Assert.Equal(ReasonCodes.NotSupported, refused.Code);
        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
        var step = Assert.Single(trace.Entries, e => e.Step == TraceSteps.AliasRefusedPrefix + ReasonCodes.NotSupported);
        Assert.Equal(ReasonCodes.OwnNumberDetail, step.Detail);
    }

    [Fact]
    public async Task AnAliasForABadlyFormedNumberIsRefusedInvalidNumber()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();

        var refused = Assert.IsType<AliasResult.Refused>(
            await SaveForPhoneAsync(Contact(before, "S03").SavedName + " x", NoWalletPhone[..^1]));

        Assert.Equal(ReasonCodes.InvalidNumber, refused.Code);
    }

    [Fact]
    public async Task AnAliasForATypedNumberOfANewPersonIsSaved()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var stranger = await NewPersonAsync(before);
        var text = Contact(before, "S03").SavedName + " x";

        var saved = Assert.IsType<AliasResult.Saved>(await SaveForPhoneAsync(text, stranger.Phone));

        Assert.Equal(stranger.AccountId, saved.Alias.TargetAccountId);
        var candidate = Assert.Single(ContactCandidate.From(await ListAsync()), c => c.AccountId == stranger.AccountId);
        Assert.True(candidate.IsAlias);
        Assert.Null(candidate.ContactCode);
        Assert.Equal(text, candidate.Text);
        Assert.Equal(AuditEvents.AliasSaved, Assert.Single(await AuditAsync()).Event);
    }

    [Fact]
    public async Task DeleteRemovesTheAliasAndWritesTheAuditRow()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var seeded = before.Aliases[0];

        Assert.IsType<AliasResult.Deleted>(await DeleteAsync(seeded.AliasId));

        Assert.DoesNotContain(seeded, (await ListAsync()).Aliases);
        var row = Assert.Single(await AuditAsync());
        Assert.Equal(AuditEvents.AliasDeleted, row.Event);
        Assert.Equal(seeded.TargetAccountId.ToString(CultureInfo.InvariantCulture), row.Detail);
        var step = Assert.Single(trace.Entries, e => e.Step == TraceSteps.AliasDeleted);
        Assert.Equal(row.Detail, step.Detail);
    }

    [Fact]
    public async Task DeletingAnUnknownAliasIsRefusedRecipientNotFound()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var before = await ListAsync();
        var unknown = before.Aliases.Max(a => a.AliasId) + 1_000;

        var refused = Assert.IsType<AliasResult.Refused>(await DeleteAsync(unknown));

        Assert.Equal(ReasonCodes.RecipientNotFound, refused.Code);
        Assert.Equal(before.Aliases, (await ListAsync()).Aliases);
        Assert.Empty(await AuditAsync());
    }

    [Fact]
    public async Task DeletingAnotherUsersAliasIsRefusedRecipientNotFound()
    {
        Assert.SkipUnless(host.DatabaseAvailable, host.SkipReason);
        var mine = (await ListAsync()).Aliases.Select(a => a.AliasId).ToHashSet();
        var theirs = Assert.IsType<WalletResult<WalletContacts>.Ok>(
            await Wallet.GetContactsAsync(SeedFacts.TraderUserId, Token)).Value.Aliases;
        var other = Assert.Single(theirs.Take(1));
        Assert.DoesNotContain(other.AliasId, mine);

        var refused = Assert.IsType<AliasResult.Refused>(await DeleteAsync(other.AliasId));

        Assert.Equal(ReasonCodes.RecipientNotFound, refused.Code);
        Assert.Contains(other, Assert.IsType<WalletResult<WalletContacts>.Ok>(
            await Wallet.GetContactsAsync(SeedFacts.TraderUserId, Token)).Value.Aliases);
    }

    private WalletClient Wallet => Provider.GetRequiredService<WalletClient>();

    private ServiceProvider Provider => provider ?? throw new InvalidOperationException(host.SkipReason);

    /// <summary>The resolver over U1's current candidates, with the settings the directory reads.</summary>
    private Resolution Resolve(string text, WalletContacts contacts)
    {
        var options = Provider.GetRequiredService<IOptionsMonitor<AgentOptions>>().CurrentValue;
        return ContactResolver.Resolve(text, ContactCandidate.From(contacts), options.MinSubstringWordLength, options.Endearments);
    }

    private static WalletContact Contact(WalletContacts contacts, string code) => Assert.Single(contacts.Contacts, c => c.Code == code);

    private async Task<WalletContacts> ListAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AliasDirectory>().ListAsync(U1, Token);
        return Assert.IsType<WalletResult<WalletContacts>.Ok>(result).Value;
    }

    private async Task<AliasResult> SaveAsync(string text, int targetAccountId)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AliasDirectory>().SaveAsync(U1, text, targetAccountId, Token);
    }

    private async Task<AliasResult> SaveForPhoneAsync(string text, string phone)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AliasDirectory>().SaveForPhoneAsync(U1, text, phone, Token);
    }

    private async Task<AliasResult> DeleteAsync(int aliasId)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AliasDirectory>().DeleteAsync(U1, aliasId, Token);
    }

    private async Task<List<AuditEntry>> AuditAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.AuditEntries.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Token);
    }

    /// <summary>
    /// An active seed account that no contact or alias of U1 points to and that is not U1's own: a new person, read
    /// back from the wallet by phone.
    /// </summary>
    private async Task<WalletAccount> NewPersonAsync(WalletContacts contacts)
    {
        var known = contacts.Contacts.Select(c => c.Phone)
            .Concat(contacts.Aliases.Select(a => a.Phone))
            .Append(SeedFacts.DemoPhone)
            .ToHashSet(StringComparer.Ordinal);
        var phone = SeedFacts.Seed.Accounts
            .First(a => a.Status == "active" && !known.Contains(a.Number))
            .Number;

        return Assert.IsType<WalletResult<WalletAccount>.Ok>(await Wallet.GetAccountByPhoneAsync(phone, Token)).Value;
    }
}
