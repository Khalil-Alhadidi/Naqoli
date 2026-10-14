using Agent.Core.Pending;

namespace Agent.Core.Confirm;

/// <summary>
/// What one Confirm tap, or one settle of a Checking record, ended in. Carries codes and figures only (rule 4); the
/// phone fills a template from its resource file.
/// </summary>
public abstract record ConfirmOutcome
{
    private ConfirmOutcome()
    {
    }

    /// <summary>
    /// The wallet confirmed the payment. <see cref="PurchaseCode"/> is the full code of a purchase, for the screen only:
    /// the pending record keeps its last four characters and nothing logs it.
    /// </summary>
    public sealed record Done(string Reference, long BalanceAfterIqd, string? PurchaseCode) : ConfirmOutcome
    {
        /// <summary>Keeps the purchase code out of any string form of the outcome.</summary>
        public override string ToString()
            => $"Done {{ Reference = {Reference}, BalanceAfterIqd = {BalanceAfterIqd}, PurchaseCode = {(PurchaseCode is null ? "none" : "***")} }}";
    }

    /// <summary>Nothing moved. <see cref="Reason"/> is one of <c>ReasonCodes</c> (or <c>pin_locked</c>), with an optional detail.</summary>
    public sealed record NotDone(string Reason, string? Detail) : ConfirmOutcome;

    /// <summary>The wallet's answer is unknown; the record is Checking, never shown as failed (rule 6).</summary>
    public sealed record Checking : ConfirmOutcome;

    /// <summary>The card's timer had ended; nothing was sent.</summary>
    public sealed record Expired : ConfirmOutcome;

    /// <summary>The record had already left Pending (a double tap, a cancel, a settled payment); nothing was sent.</summary>
    public sealed record AlreadyHandled(PendingState State) : ConfirmOutcome;

    /// <summary>No pending record has this ID.</summary>
    public sealed record NotFound : ConfirmOutcome;

    /// <summary>The payment needs a PIN and none was given; the card stays open.</summary>
    public sealed record PinRequired : ConfirmOutcome;

    /// <summary>The PIN was wrong; the card stays open with <see cref="AttemptsLeft"/> attempts.</summary>
    public sealed record PinWrong(int AttemptsLeft) : ConfirmOutcome;

    /// <summary>The PIN was not 4 to 6 digits; no attempt was counted and the card stays open.</summary>
    public sealed record PinMalformed : ConfirmOutcome;
}
