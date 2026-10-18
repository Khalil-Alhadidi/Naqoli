using System.Globalization;
using Agent.Core.Audit;
using Agent.Core.Pipeline;
using Agent.Core.Resolver;
using Agent.Core.Text;
using Agent.Core.Trace;
using Agent.Core.Wallet;
using Microsoft.Extensions.Options;

namespace Agent.Core.Aliases;

/// <summary>
/// Lists, saves and deletes a user's aliases through the wallet (milestone 9a, D15). One alias points to exactly one
/// account: a text whose key (<see cref="ArabicText.Key"/>) equals the key of any saved name or alias the user already
/// has, on any account, is refused <see cref="ReasonCodes.AliasExists"/> before the wallet is written (ruling 4), so an
/// alias never makes an exact hit on two accounts and never shadows a saved name. Moves no money and touches no pending
/// record. Every save and delete writes its audit row and trace step with the account id, never the alias text.
/// </summary>
public sealed class AliasDirectory
{
    private readonly WalletClient wallet;
    private readonly AuditLog audit;
    private readonly ITraceSink trace;
    private readonly IOptionsMonitor<AgentOptions> options;

    public AliasDirectory(WalletClient wallet, AuditLog audit, ITraceSink trace, IOptionsMonitor<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(options);

        this.wallet = wallet;
        this.audit = audit;
        this.trace = trace;
        this.options = options;
    }

    /// <summary>The user's contacts and aliases, as the wallet holds them.</summary>
    public Task<WalletResult<WalletContacts>> ListAsync(int userId, CancellationToken cancellationToken)
        => wallet.GetContactsAsync(userId, cancellationToken);

    /// <summary>
    /// Saves <paramref name="text"/> (trimmed) as the user's alias of <paramref name="targetAccountId"/>, which must be the
    /// account of one of the user's contacts or aliases. Checked in this order: <see cref="ReasonCodes.AliasInvalid"/>
    /// (the text), the contacts read, <see cref="ReasonCodes.AliasExists"/>, <see cref="ReasonCodes.AliasShared"/> (one
    /// word that the resolver answers with a question on several accounts, review R4),
    /// <see cref="ReasonCodes.RecipientNotFound"/> (the account is not among the user's contacts or aliases), then
    /// <see cref="ReasonCodes.AccountClosed"/> (the account is closed, review R2). Nothing is written on a refusal.
    /// </summary>
    public async Task<AliasResult> SaveAsync(int userId, string text, int targetAccountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!TryValidate(text, out var trimmed))
            return Refuse(ReasonCodes.AliasInvalid);

        if (await ContactsAsync(userId, cancellationToken) is not { } contacts)
            return NoList();

        var candidates = ContactCandidate.From(contacts);
        if (Exists(candidates, trimmed))
            return Refuse(ReasonCodes.AliasExists);

        if (IsShared(candidates, trimmed))
            return Refuse(ReasonCodes.AliasShared);

        var targets = candidates.Where(c => c.AccountId == targetAccountId).ToList();
        if (targets.Count == 0)
            return Refuse(ReasonCodes.RecipientNotFound);

        // Review R2: an alias for a closed account could only ever end in a refusal; nothing is written.
        if (targets.Any(c => IsClosed(c.AccountStatus)))
            return Refuse(ReasonCodes.AccountClosed);

        return await WriteAsync(userId, trimmed, targetAccountId, cancellationToken);
    }

    /// <summary>
    /// Saves <paramref name="text"/> (trimmed) as the user's alias of the account behind a typed
    /// <paramref name="phone"/> (D21), contact or not. Checked in this order, pure checks before any wallet read
    /// (security review M3): <see cref="ReasonCodes.InvalidNumber"/> (not a complete number),
    /// <see cref="ReasonCodes.AliasInvalid"/> (the text), <see cref="ReasonCodes.NotSupported"/> (the user's own number),
    /// <see cref="ReasonCodes.NumberHasNoWallet"/>, <see cref="ReasonCodes.AccountClosed"/> (the account behind the number
    /// is closed, review R2, before the contacts are read), then <see cref="ReasonCodes.AliasExists"/> and
    /// <see cref="ReasonCodes.AliasShared"/> as in <see cref="SaveAsync"/>.
    /// </summary>
    public async Task<AliasResult> SaveForPhoneAsync(int userId, string text, string phone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!PhoneNumbers.TryNormalize(phone, out var normalized))
            return Refuse(ReasonCodes.InvalidNumber);

        if (!TryValidate(text, out var trimmed))
            return Refuse(ReasonCodes.AliasInvalid);

        // The user's own number is refused before the wallet is asked about it, as for a transfer.
        switch (await wallet.GetUserAsync(userId, cancellationToken))
        {
            case WalletResult<WalletUser>.Ok { Value: var user } when string.Equals(user.Phone, normalized, StringComparison.Ordinal):
                return Refuse(ReasonCodes.NotSupported, ReasonCodes.OwnNumberDetail);
            case WalletResult<WalletUser>.Ok:
                break;
            case WalletResult<WalletUser>.Unavailable unavailable:
                return Unavailable(unavailable.Reason);
            default:
                return Unavailable(null);
        }

        WalletAccount account;
        switch (await wallet.GetAccountByPhoneAsync(normalized, cancellationToken))
        {
            case WalletResult<WalletAccount>.Ok ok:
                account = ok.Value;
                break;
            case WalletResult<WalletAccount>.Refused { Problem.Status: 404 }:
                return Refuse(ReasonCodes.NumberHasNoWallet);
            case WalletResult<WalletAccount>.Refused:
                return Refuse(ReasonCodes.InvalidNumber);
            case WalletResult<WalletAccount>.Unavailable unavailable:
                return Unavailable(unavailable.Reason);
            default:
                return Unavailable(null);
        }

        // Review R2: the account behind the number is closed; checked before the contacts are read.
        if (IsClosed(account.Status))
            return Refuse(ReasonCodes.AccountClosed);

        if (await ContactsAsync(userId, cancellationToken) is not { } contacts)
            return NoList();

        var candidates = ContactCandidate.From(contacts);
        if (Exists(candidates, trimmed))
            return Refuse(ReasonCodes.AliasExists);

        if (IsShared(candidates, trimmed))
            return Refuse(ReasonCodes.AliasShared);

        return await WriteAsync(userId, trimmed, account.AccountId, cancellationToken);
    }

    /// <summary>
    /// Deletes one of the user's aliases. An id that is not among the user's aliases is refused
    /// <see cref="ReasonCodes.RecipientNotFound"/> without a wallet write.
    /// </summary>
    public async Task<AliasResult> DeleteAsync(int userId, int aliasId, CancellationToken cancellationToken)
    {
        if (await ContactsAsync(userId, cancellationToken) is not { } contacts)
            return NoList();

        if (contacts.Aliases.FirstOrDefault(a => a.AliasId == aliasId) is not { } alias)
            return Refuse(ReasonCodes.RecipientNotFound);

        switch (await wallet.DeleteAliasAsync(userId, aliasId, cancellationToken))
        {
            case WalletResult<bool>.Ok:
                await audit.WriteAliasAsync(userId, AuditEvents.AliasDeleted, alias.TargetAccountId, cancellationToken);
                trace.Record(TraceZone.Code, TraceSteps.AliasDeleted, Invariant(alias.TargetAccountId));
                return new AliasResult.Deleted();
            case WalletResult<bool>.Refused:
                // 404 unknown_alias: deleted meanwhile, or not the user's.
                return Refuse(ReasonCodes.RecipientNotFound);
            case WalletResult<bool>.Unavailable unavailable:
                return Unavailable(unavailable.Reason);
            default:
                return Unavailable(null);
        }
    }

    private async Task<AliasResult> WriteAsync(int userId, string text, int accountId, CancellationToken cancellationToken)
    {
        switch (await wallet.AddAliasAsync(userId, new WalletAliasRequest(text, accountId), cancellationToken))
        {
            case WalletResult<WalletAlias>.Ok ok:
                await audit.WriteAliasAsync(userId, AuditEvents.AliasSaved, ok.Value.TargetAccountId, cancellationToken);
                trace.Record(TraceZone.Code, TraceSteps.AliasSaved, Invariant(ok.Value.TargetAccountId));
                return new AliasResult.Saved(ok.Value);
            case WalletResult<WalletAlias>.Refused { Problem.Status: 409 }:
                return Refuse(ReasonCodes.AliasExists);
            case WalletResult<WalletAlias>.Refused { Problem.Status: 422 }:
                return Refuse(ReasonCodes.AliasInvalid);
            case WalletResult<WalletAlias>.Refused:
                // 404 unknown_account (or unknown_user): no such account to point to.
                return Refuse(ReasonCodes.RecipientNotFound);
            case WalletResult<WalletAlias>.Unavailable unavailable:
                return Unavailable(unavailable.Reason);
            default:
                return Unavailable(null);
        }
    }

    /// <summary>
    /// Ruling 4: not blank, at most <see cref="AgentOptions.MaxAliasLength"/> characters after trimming, a key of at
    /// least one letter, and not a phone number attempt.
    /// </summary>
    private bool TryValidate(string text, out string trimmed)
    {
        trimmed = text.Trim();
        return trimmed.Length > 0
            && trimmed.Length <= options.CurrentValue.MaxAliasLength
            && ArabicText.Key(trimmed).Length > 0
            && !PhoneNumbers.LooksLikeNumber(trimmed);
    }

    /// <summary>The wallet's closed status, compared as <see cref="Checks"/> compares it.</summary>
    private static bool IsClosed(string status) => string.Equals(status, Checks.ClosedStatus, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Review R4 (Khalil, 18 Oct 2026): a one-word text that the resolver, over the user's current candidates and with the
    /// current settings, answers with a question on more than one account (the partial step on several, rule 4; the
    /// shared-word step, rule 15; or the near step on several). Saved, it would go straight to one account where the
    /// user would have been asked. A text of two or more words is never refused here, so rule 18's offers still save.
    /// </summary>
    private bool IsShared(IReadOnlyList<ContactCandidate> candidates, string text)
    {
        if (ArabicText.Words(text).Count != 1)
            return false;

        var current = options.CurrentValue;
        return ContactResolver.Resolve(text, candidates, current.MinSubstringWordLength, current.Endearments)
            is Resolution.Options { Candidates.Count: > 1 };
    }

    private static bool Exists(IReadOnlyList<ContactCandidate> candidates, string text)
    {
        var key = ArabicText.Key(text);
        return candidates.Any(c => string.Equals(ArabicText.Key(c.Text), key, StringComparison.Ordinal));
    }

    /// <summary>The user's contacts and aliases, or null when the wallet gave none (traced).</summary>
    private async Task<WalletContacts?> ContactsAsync(int userId, CancellationToken cancellationToken)
    {
        switch (await wallet.GetContactsAsync(userId, cancellationToken))
        {
            case WalletResult<WalletContacts>.Ok ok:
                return ok.Value;
            case WalletResult<WalletContacts>.Unavailable unavailable:
                Unavailable(unavailable.Reason);
                return null;
            default:
                // A refusal (unknown_user) is no list to check against either.
                Unavailable(null);
                return null;
        }
    }

    private AliasResult.Refused Refuse(string code, string? detail = null)
    {
        trace.Record(TraceZone.Code, TraceSteps.AliasRefusedPrefix + code, detail);
        return new AliasResult.Refused(code);
    }

    private AliasResult.Unavailable Unavailable(string? reason)
    {
        trace.Record(
            TraceZone.Code,
            TraceSteps.AliasRefusedPrefix + ReasonCodes.NotDoneNothingTaken,
            ReasonCodes.WalletDetailPrefix + (reason ?? "refused"));
        return new AliasResult.Unavailable();
    }

    /// <summary>The result after <see cref="ContactsAsync"/> has already traced why it gave no list.</summary>
    private static AliasResult.Unavailable NoList() => new();

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
