namespace Agent.Core.Trace;

/// <summary>The step names the orchestrator and the Confirm handler write to the trace.</summary>
public static class TraceSteps
{
    // Code zone: the orchestrator.
    public const string ParseReceived = "parse_received";
    public const string ConsentChecked = "consent_checked";

    /// <summary>
    /// A message typed after the consent screen's No and before Agree (Khalil, 20 Oct 2026): not read; the consent
    /// screen is shown again.
    /// </summary>
    public const string ConsentRequiredBlocked = "consent_required_blocked";

    public const string RecipientResolved = "recipient_resolved";
    public const string AmountResolved = "amount_resolved";
    public const string ChecksPassed = "checks_passed";
    public const string CheckFailedPrefix = "check_failed:";
    public const string CardCreated = "card_created";
    public const string QuestionPrefix = "question:";
    public const string RefusalPrefix = "refusal:";
    public const string BalanceShown = "balance_shown";

    /// <summary>The kill switch stopped the request or the settle; the detail is <c>on</c>.</summary>
    public const string KillSwitch = "kill_switch";

    /// <summary>A card rebuilt from its stored record (D19); the detail is <c>try_again</c> or <c>send_again</c>.</summary>
    public const string Reissue = "reissue";

    /// <summary>A known gap handled with a card and a note; the detail is the gap's code (<c>choose_account</c>).</summary>
    public const string KnownGap = "known_gap";

    // Code zone: aliases (milestone 9a). The detail is an account id, a code or none, never the alias text.

    /// <summary>An alias was offered under a Done outcome (rule 18); no detail.</summary>
    public const string AliasOffered = "alias_offered";

    /// <summary>An alias was saved; the detail is the account id.</summary>
    public const string AliasSaved = "alias_saved";

    /// <summary>An alias was deleted; the detail is the account id.</summary>
    public const string AliasDeleted = "alias_deleted";

    /// <summary>An alias save or delete was refused; the step ends with the reason code (<c>alias_refused:alias_exists</c>).</summary>
    public const string AliasRefusedPrefix = "alias_refused:";

    // Code zone: store purchases (milestone 9b). The detail is a count, never a purchase code.

    /// <summary>The user's orders were read for My orders; the detail is the number of orders.</summary>
    public const string OrdersRead = "orders_read";

    // Code zone: the conversation (milestone 9b close-out, 19 Oct 2026).

    /// <summary>A message of four to six digits typed while a PIN card is live: pin_required, no parse call; no detail.</summary>
    public const string PinTypedInChat = "pin_typed_in_chat";

    /// <summary>A typed message longer than <c>MaxMessageChars</c>: message_too_long, no parse call; the detail is the length.</summary>
    public const string MessageTooLong = "message_too_long";

    // Code zone: compound requests (milestone 9c, D7). The detail is a count, never a request's text.

    /// <summary>A message with more than one request was split; the detail is <c>&lt;total&gt; requests, &lt;kept&gt; kept</c>.</summary>
    public const string RequestsSplit = "requests_split";

    /// <summary>A queued part runs because the previous one finished; the detail is <c>&lt;n&gt; of &lt;kept&gt;</c>.</summary>
    public const string NextRequest = "next_request";

    /// <summary>A new request dropped the parts still queued; the detail is how many.</summary>
    public const string RequestsDropped = "requests_dropped";

    /// <summary>
    /// A Confirm or Cancel tap named a card that is not the one shown (ruling 8): <c>stale_card</c>, or <c>no_card</c>
    /// when none is shown. Nothing moves.
    /// </summary>
    public const string TapRejected = "tap_rejected";

    // Write zone: the Confirm handler.
    public const string BeginExecute = "begin_execute";
    public const string RecheckOk = "recheck_ok";
    public const string RecheckFailedPrefix = "recheck_failed:";
    public const string ExecuteSent = "execute_sent";
    public const string OutcomePrefix = "outcome:";
    public const string SettleSent = "settle_sent";

    /// <summary>
    /// The by-key lookup before a settle's resend (milestone 8): <c>done</c>, <c>failed</c>, <c>unknown_key</c> or
    /// <c>unavailable</c> and the reason. Display only: the outcome comes from the resend.
    /// </summary>
    public const string LookupByKey = "lookup_by_key";
    public const string SettleOutcomePrefix = "settle_outcome:";
}
