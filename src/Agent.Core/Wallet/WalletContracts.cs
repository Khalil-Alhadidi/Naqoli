namespace Agent.Core.Wallet;

// The agent's own copies of the mock wallet's JSON shapes (docs/wallet-api.md). The agent talks to the wallet over HTTP
// only, so it never shares the wallet's types. Names are camelCase on the wire (JsonSerializerDefaults.Web).

/// <summary>A wallet holder (<c>GET /users/{id}</c>).</summary>
public sealed record WalletUser(int Id, string Code, string Phone, string RegisteredName, bool ConsentGiven);

/// <summary>The account behind a phone number (<c>GET /accounts/by-phone/{phone}</c>). Status is <c>active</c> or <c>closed</c>.</summary>
public sealed record WalletAccount(int AccountId, string Phone, string? RegisteredName, string Status);

/// <summary>A saved contact, resolved to its account, with the last completed transfer to it.</summary>
public sealed record WalletContact(
    int ContactId,
    string Code,
    string SavedName,
    string Phone,
    string? RegisteredName,
    string AccountStatus,
    int TargetAccountId,
    DateTimeOffset? LastSentAt,
    long? LastSentAmountIqd);

/// <summary>An alias and the one account it points to.</summary>
public sealed record WalletAlias(
    int AliasId,
    string Text,
    int TargetAccountId,
    string Phone,
    string? RegisteredName,
    string AccountStatus);

/// <summary>The contacts and aliases of a user (<c>GET /users/{id}/contacts</c>).</summary>
public sealed record WalletContacts(IReadOnlyList<WalletContact> Contacts, IReadOnlyList<WalletAlias> Aliases);

/// <summary>A bill account of a user with the amount due; zero means nothing is due.</summary>
public sealed record WalletBill(
    int BillAccountId,
    string Code,
    string BillerCode,
    string BillerName,
    string BillerNameEn,
    string? Label,
    string AccountNumber,
    long AmountDueIqd);

/// <summary>The balance of a user's default account at a moment.</summary>
public sealed record WalletBalance(int AccountId, long BalanceIqd, DateTimeOffset AsOf);

/// <summary>One past transaction paid by a user (<c>GET /users/{id}/history</c>).</summary>
public sealed record WalletHistoryItem(
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

/// <summary>A store catalogue item (<c>GET /store/products</c>).</summary>
public sealed record WalletProduct(
    int ProductId,
    string Code,
    string Provider,
    string ProviderNameEn,
    string ProviderNameAr,
    string? AlsoCalled,
    string Type,
    long FaceValueIqd,
    long PriceIqd);

/// <summary>
/// The body of <c>POST /quotes</c> and <c>POST /transactions</c>. <see cref="Type"/> is <c>transfer</c>, <c>bill</c>
/// or <c>purchase</c>. <see cref="Pin"/> travels only in this body, on execute.
/// </summary>
public sealed record WalletPaymentRequest(
    int UserId,
    string Type,
    long? AmountIqd,
    string? ToPhone,
    int? ToAccountId,
    int? BillAccountId,
    int? ProductId,
    string? Pin)
{
    /// <summary>Keeps the PIN out of any string form of the request.</summary>
    public override string ToString()
        => $"WalletPaymentRequest {{ UserId = {UserId}, Type = {Type}, AmountIqd = {AmountIqd}, ToAccountId = {ToAccountId}, BillAccountId = {BillAccountId}, ProductId = {ProductId}, Pin = {(Pin is null ? "none" : "***")} }}";
}

/// <summary>The fee, total and balance after for a payment that has not happened (<c>POST /quotes</c>).</summary>
public sealed record WalletQuote(
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

/// <summary>
/// An executed transaction, a replay of one, or a lookup by key. Status is <c>executing</c>, <c>done</c> or
/// <c>failed</c>; <see cref="Code"/> is the purchase code of a purchase.
/// </summary>
public sealed record WalletTransaction(
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
