namespace Agent.Core.Pending;

/// <summary>
/// What the orchestrator hands the <see cref="PendingStore"/> to open a card: every field is built by code from the
/// wallet's quote and the resolver (rule 3), never from model text. The store adds the ID, the key and the timer.
/// </summary>
public sealed record CardDraft(
    int UserId,
    string Type,
    long AmountIqd,
    long FeeIqd,
    long TotalIqd,
    long QuotedBalanceIqd,
    string? ToPhone,
    int? ToAccountId,
    string? DisplayName,
    string? RegisteredName,
    int? BillAccountId,
    string? BillCode,
    int? ProductId,
    string? ProductCode,
    string? ProductType,
    bool PinRequired,
    bool FirstTime,
    string? Warning);
