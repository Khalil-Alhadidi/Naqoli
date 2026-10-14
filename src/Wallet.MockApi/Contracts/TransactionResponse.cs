namespace Wallet.MockApi.Contracts;

/// <summary>
/// The outcome of an executed transaction, and of a lookup or replay by idempotency key. <see cref="Status"/> is
/// <c>executing</c>, <c>done</c> or <c>failed</c>. <see cref="BalanceAfterIqd"/> is the payer's balance right after
/// that payment, stored with it; for seeded rows, which store none, the balance now. <see cref="Code"/> is the purchase code, for purchases only. <see cref="ErrorCode"/> is the
/// stored refusal code (<c>insufficient_funds</c> or <c>nothing_due</c>) of a <c>failed</c> row, and null otherwise.
/// </summary>
public sealed record TransactionResponse(
    int TransactionId,
    string Reference,
    string Type,
    string Status,
    long AmountIqd,
    long FeeIqd,
    long TotalIqd,
    long BalanceAfterIqd,
    string? Code,
    DateTimeOffset CompletedAt,
    string? ErrorCode);
