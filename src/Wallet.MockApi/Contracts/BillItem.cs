namespace Wallet.MockApi.Contracts;

/// <summary>A user's account with a biller and the amount due; zero means nothing is due.</summary>
public sealed record BillItem(
    int BillAccountId,
    string BillerCode,
    string BillerName,
    string? Label,
    string AccountNumber,
    long AmountDueIqd);
