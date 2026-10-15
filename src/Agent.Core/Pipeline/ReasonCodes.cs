namespace Agent.Core.Pipeline;

/// <summary>
/// The reason codes a reply carries, exactly as Khalil lists them in <c>data/README.md</c> (Reason codes). Replies are
/// graded on these codes, never on wording (rule 4: the wording comes from the phone resource file).
/// </summary>
public static class ReasonCodes
{
    // Refusals.
    public const string InsufficientBalance = "insufficient_balance";
    public const string RecipientNotFound = "recipient_not_found";
    public const string NumberHasNoWallet = "number_has_no_wallet";
    public const string InvalidNumber = "invalid_number";
    public const string OverPerTransactionCap = "over_per_transaction_cap";
    public const string OverDailyCap = "over_daily_cap";
    public const string AccountClosed = "account_closed";
    public const string ConsentRequired = "consent_required";
    public const string NothingDue = "nothing_due";
    public const string BillNotFound = "bill_not_found";
    public const string BillerUnavailable = "biller_unavailable";
    public const string ProductNotFound = "product_not_found";
    public const string NotSupported = "not_supported";
    public const string NotUnderstood = "not_understood";
    public const string OnlyOwnBalance = "only_own_balance";
    public const string ModelUnavailable = "model_unavailable";
    public const string DinarsOnly = "dinars_only";
    public const string BillsPaidInFull = "bills_paid_in_full";

    // Informational.
    public const string Balance = "balance";
    public const string PointsToConfirm = "points_to_confirm";
    public const string CardCancelled = "card_cancelled";
    public const string CannotChangeCard = "cannot_change_card";
    public const string AccountChoiceUnavailable = "account_choice_unavailable";
    public const string CodeEnteredByUser = "code_entered_by_user";
    public const string MaxThreeRequests = "max_three_requests";
    public const string NotDoneNothingTaken = "not_done_nothing_taken";

    /// <summary>
    /// An amount below <see cref="AgentOptions.MinAmountIqd"/>. Provisional, not in Khalil's list (data/README.md);
    /// reported to Khalil. The rules say such an amount is refused but name no code.
    /// </summary>
    public const string AmountBelowMinimum = "amount_below_minimum";

    /// <summary>The detail of a <see cref="NotSupported"/> refusal for a transfer to the user's own number.</summary>
    public const string OwnNumberDetail = "own_number";

    /// <summary>The detail of <see cref="NotDoneNothingTaken"/> while the kill switch is on (<see cref="KillSwitch"/>).</summary>
    public const string KillSwitchDetail = "kill_switch";

    /// <summary>The prefix of the detail of <see cref="NotDoneNothingTaken"/> when a wallet read or quote gave no answer.</summary>
    public const string WalletDetailPrefix = "wallet_";
}
