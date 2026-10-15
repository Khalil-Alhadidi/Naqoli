using Agent.Core.Pending;
using Agent.Core.Wallet;
using Microsoft.Extensions.Options;

namespace Agent.Core.Pipeline;

/// <summary>
/// What a card is for, as the orchestrator resolved it from the wallet's data (never from model text, rule 3).
/// <see cref="DisplayName"/> is the alias or saved name of a contact, the typed phone of a new number, or the bill's
/// label or biller, or the product's operator; <see cref="RegisteredName"/> is the recipient account's registered name.
/// </summary>
public sealed record CardSubject(
    string Type,
    string? ToPhone,
    int? ToAccountId,
    string? DisplayName,
    string? RegisteredName,
    int? BillAccountId,
    string? BillCode,
    int? ProductId,
    string? ProductCode);

/// <summary>
/// Builds the <see cref="CardDraft"/> of a payment that passed the checks: the figures from the wallet's quote, the
/// "first time" line (D16) and the repeat warning (FLT-05) from the user's history.
/// </summary>
public sealed class CardBuilder
{
    /// <summary>The resource key of the repeat warning on a card.</summary>
    public const string RepeatWarning = "repeat";

    private const string DoneStatus = "done";
    private const string TransferType = "transfer";

    private readonly WalletClient wallet;
    private readonly AgentOptions options;

    public CardBuilder(WalletClient wallet, IOptionsSnapshot<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(options);

        this.wallet = wallet;
        this.options = options.Value;
    }

    /// <summary>
    /// The draft for <paramref name="subject"/> with the figures of <paramref name="quote"/>. For a transfer it reads the
    /// done transfers to the same phone over the last <see cref="AgentOptions.HistoryDays"/> days: none means
    /// <see cref="CardDraft.FirstTime"/>; one of the same amount within <see cref="AgentOptions.RepeatWarningWindowMinutes"/>
    /// before <paramref name="now"/> means <see cref="RepeatWarning"/>. Bills and purchases are never first time. A
    /// history read with no answer, or refused, is returned as such; nothing else is read.
    /// </summary>
    public async Task<WalletResult<CardDraft>> BuildAsync(
        int userId, WalletQuote quote, CardSubject subject, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(subject);

        var firstTime = false;
        string? warning = null;

        if (string.Equals(subject.Type, TransferType, StringComparison.Ordinal) && subject.ToPhone is not null)
        {
            var history = await wallet.GetHistoryAsync(
                userId, now.AddDays(-options.HistoryDays), subject.ToPhone, options.HistoryPageSize, cancellationToken);
            switch (history)
            {
                case WalletResult<IReadOnlyList<WalletHistoryItem>>.Ok { Value: var items }:
                    var sent = items
                        .Where(h => string.Equals(h.Status, DoneStatus, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(h.Type, TransferType, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(h.CounterpartyPhone, subject.ToPhone, StringComparison.Ordinal))
                        .ToList();
                    firstTime = sent.Count == 0;
                    var windowStart = now.AddMinutes(-options.RepeatWarningWindowMinutes);
                    if (sent.Any(h => h.AmountIqd == quote.AmountIqd && h.CreatedAt >= windowStart))
                        warning = RepeatWarning;
                    break;
                case WalletResult<IReadOnlyList<WalletHistoryItem>>.Unavailable unavailable:
                    return new WalletResult<CardDraft>.Unavailable(unavailable.Reason);
                case WalletResult<IReadOnlyList<WalletHistoryItem>>.Refused refused:
                    return new WalletResult<CardDraft>.Refused(refused.Problem);
                default:
                    throw new InvalidOperationException("Unknown wallet result.");
            }
        }

        return new WalletResult<CardDraft>.Ok(new CardDraft(
            UserId: userId,
            Type: subject.Type,
            AmountIqd: quote.AmountIqd,
            FeeIqd: quote.FeeIqd,
            TotalIqd: quote.TotalIqd,
            QuotedBalanceIqd: quote.BalanceBeforeIqd,
            ToPhone: subject.ToPhone,
            ToAccountId: subject.ToAccountId,
            DisplayName: subject.DisplayName,
            RegisteredName: subject.RegisteredName ?? quote.RecipientName,
            BillAccountId: subject.BillAccountId,
            BillCode: subject.BillCode,
            ProductId: subject.ProductId,
            ProductCode: subject.ProductCode,
            PinRequired: quote.PinRequired,
            FirstTime: firstTime,
            Warning: warning));
    }
}
