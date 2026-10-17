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
    /// account of one of the user's contacts or aliases. Refused <see cref="ReasonCodes.AliasInvalid"/>,
    /// <see cref="ReasonCodes.AliasExists"/> or <see cref="ReasonCodes.RecipientNotFound"/>.
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

        if (!candidates.Any(c => c.AccountId == targetAccountId))
            return Refuse(ReasonCodes.RecipientNotFound);

        return await WriteAsync(userId, trimmed, targetAccountId, cancellationToken);
    }

    /// <summary>
    /// Saves <paramref name="text"/> (trimmed) as the user's alias of the account behind a typed
    /// <paramref name="phone"/> (D21), contact or not. Refused <see cref="ReasonCodes.InvalidNumber"/> (not a complete
    /// number), <see cref="ReasonCodes.NotSupported"/> (the user's own number), <see cref="ReasonCodes.NumberHasNoWallet"/>,
    /// then as <see cref="SaveAsync"/> for the text.
    /// </summary>
    public async Task<AliasResult> SaveForPhoneAsync(int userId, string text, string phone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!PhoneNumbers.TryNormalize(phone, out var normalized))
            return Refuse(ReasonCodes.InvalidNumber);

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

        if (!TryValidate(text, out var trimmed))
            return Refuse(ReasonCodes.AliasInvalid);

        if (await ContactsAsync(userId, cancellationToken) is not { } contacts)
            return NoList();

        if (Exists(ContactCandidate.From(contacts), trimmed))
            return Refuse(ReasonCodes.AliasExists);

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
