namespace Wallet.MockApi.Contracts;

/// <summary>
/// The wallet's own limits, as configured (<c>Wallet</c> section): the fee of each type, the amount above which the PIN
/// is needed, and the wrong PIN entries allowed. Read only; the agent shows them in its settings component.
/// </summary>
public sealed record SettingsResponse(
    long TransferFeeIqd, long BillFeeIqd, long PurchaseFeeIqd, long PinRequiredAboveIqd, int MaxPinAttempts);
