namespace Wallet.MockApi.Contracts;

/// <summary>
/// The fee, total and balance after for a payment that has not happened. No money moves. A quote never refuses
/// on funds: <see cref="Sufficient"/> says whether the balance covers the total, and only execute refuses.
/// </summary>
public sealed record QuoteResponse(
    string Type,
    long AmountIqd,
    long FeeIqd,
    long TotalIqd,
    long BalanceBeforeIqd,
    long BalanceAfterIqd,
    bool Sufficient,
    bool PinRequired,
    string? RecipientPhone,
    string? RecipientName,
    int? BillAccountId,
    int? ProductId);
