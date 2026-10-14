namespace Wallet.MockApi.Contracts;

/// <summary>One past transaction paid by a user. The counterparty is set for transfers only (the recipient account).</summary>
public sealed record HistoryItem(
    int TransactionId,
    string Reference,
    string Type,
    string Status,
    long AmountIqd,
    long FeeIqd,
    long TotalIqd,
    string? CounterpartyPhone,
    string? CounterpartyName,
    int? CounterpartyAccountId,
    int? BillAccountId,
    int? ProductId,
    DateTimeOffset CreatedAt);
