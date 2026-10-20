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

    /// <summary>
    /// A typed message longer than <see cref="AgentOptions.MaxMessageChars"/>: refused before masking, with no parse call
    /// and no learning row (known gaps row 36; its own string, Khalil, 19 Oct 2026). Not in Khalil's list (data/README.md);
    /// not a logged reason.
    /// </summary>
    public const string MessageTooLong = "message_too_long";

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

    /// <summary>
    /// The user tapped the cancel option of a question (<c>Option.CancelQuestion</c>): the pending request is cleared and
    /// nothing was taken. Not in Khalil's list (data/README.md); not a logged reason.
    /// </summary>
    public const string QuestionCancelled = "question_cancelled";

    /// <summary>
    /// A typed answer to a typed question that the code could not read and the parse call read as a partial transfer
    /// merged into the pending transfer, and the parts still queued (or not prepared) of the message before were dropped
    /// (D7, ruling 5): the phone says so before the merged request runs (review R2 of milestone 9c, Khalil, 20 Oct 2026).
    /// Informational; a new request that drops the queue says nothing. Not in Khalil's list (data/README.md); not a
    /// logged reason.
    /// </summary>
    public const string QueueDropped = "queue_dropped";

    /// <summary>
    /// An alias refused because its key (<see cref="Text.ArabicText.Key"/>) equals the key of a saved name or alias the
    /// user already has, or the wallet holds that text already (milestone 9a, ruling 4, D15). Not a logged reason.
    /// </summary>
    public const string AliasExists = "alias_exists";

    /// <summary>
    /// An alias refused because its text is blank, longer than <see cref="AgentOptions.MaxAliasLength"/>, has a key of
    /// zero length, or looks like a phone number (milestone 9a, ruling 4). Not a logged reason.
    /// </summary>
    public const string AliasInvalid = "alias_invalid";

    /// <summary>
    /// An alias refused because its text is one word that the resolver, over the user's current saved names and aliases,
    /// answers with a question on more than one account (a bare name shared by several: the partial step, rule 4; the
    /// shared-word step, rule 15; or the near step on several), so saving it would skip that question (milestone 9a
    /// close-out, review R4, Khalil, 18 Oct 2026). Not a logged reason.
    /// </summary>
    public const string AliasShared = "alias_shared";

    /// <summary>The detail of a <see cref="NotSupported"/> refusal for a transfer to the user's own number.</summary>
    public const string OwnNumberDetail = "own_number";

    /// <summary>The detail of <see cref="NotDoneNothingTaken"/> while the kill switch is on (<see cref="KillSwitch"/>).</summary>
    public const string KillSwitchDetail = "kill_switch";

    /// <summary>The prefix of the detail of <see cref="NotDoneNothingTaken"/> when a wallet read or quote gave no answer.</summary>
    public const string WalletDetailPrefix = "wallet_";
}
