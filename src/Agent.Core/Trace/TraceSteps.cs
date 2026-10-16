namespace Agent.Core.Trace;

/// <summary>The step names the orchestrator and the Confirm handler write to the trace.</summary>
public static class TraceSteps
{
    // Code zone: the orchestrator.
    public const string ParseReceived = "parse_received";
    public const string ConsentChecked = "consent_checked";
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

    // Write zone: the Confirm handler.
    public const string BeginExecute = "begin_execute";
    public const string RecheckOk = "recheck_ok";
    public const string RecheckFailedPrefix = "recheck_failed:";
    public const string ExecuteSent = "execute_sent";
    public const string OutcomePrefix = "outcome:";
    public const string SettleSent = "settle_sent";
    public const string SettleOutcomePrefix = "settle_outcome:";
}
