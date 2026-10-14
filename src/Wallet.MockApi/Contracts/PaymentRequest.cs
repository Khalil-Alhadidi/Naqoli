namespace Wallet.MockApi.Contracts;

/// <summary>
/// The body of <c>POST /quotes</c> and <c>POST /transactions</c>. <see cref="Type"/> is <c>transfer</c>,
/// <c>bill</c> or <c>purchase</c>. A transfer names its recipient by <see cref="ToPhone"/> or
/// <see cref="ToAccountId"/> and needs <see cref="AmountIqd"/>; a bill names <see cref="BillAccountId"/> and
/// a purchase <see cref="ProductId"/>, and their amounts come from the wallet. <see cref="Pin"/> is checked
/// on execute only and is never echoed or logged.
/// </summary>
public sealed record PaymentRequest(
    int UserId,
    string? Type,
    long? AmountIqd,
    string? ToPhone,
    int? ToAccountId,
    int? BillAccountId,
    int? ProductId,
    string? Pin)
{
    /// <summary>Keeps the PIN out of any accidental string form of the request.</summary>
    public override string ToString()
        => $"PaymentRequest {{ UserId = {UserId}, Type = {Type}, AmountIqd = {AmountIqd}, BillAccountId = {BillAccountId}, ProductId = {ProductId} }}";
}
