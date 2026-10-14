namespace Wallet.MockApi.Contracts;

/// <summary>
/// The outcome of an executed transaction, and of a lookup or replay by idempotency key. <see cref="Status"/> is
/// <c>executing</c>, <c>done</c> or <c>failed</c>. <see cref="BalanceAfterIqd"/> is the payer's balance when the
/// response was made. <see cref="Code"/> is the purchase code, for purchases only.
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
    DateTimeOffset CompletedAt);
