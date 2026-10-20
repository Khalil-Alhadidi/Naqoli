namespace Agent.Core.Audit;

/// <summary>The event names the audit log stores in <see cref="AuditEntry.Event"/>.</summary>
public static class AuditEvents
{
    public const string Created = "created";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string Executing = "executing";
    public const string Done = "done";
    public const string NotDone = "not_done";
    public const string Checking = "checking";
    public const string BackToPending = "back_to_pending";
    public const string BalanceShown = "balance_shown";
    public const string QuestionAsked = "question_asked";
    public const string Refused = "refused";
    public const string ConsentShown = "consent_shown";

    /// <summary>
    /// The consent screen's No button was tapped (Khalil, 20 Oct 2026): nothing was sent to the wallet; the reason is
    /// <c>consent_required</c>.
    /// </summary>
    public const string ConsentDeclined = "consent_declined";

    /// <summary>The kill switch was flipped in the console; the reason is <c>on</c> or <c>off</c>.</summary>
    public const string KillSwitch = "kill_switch";

    /// <summary>An alias was saved in the wallet (milestone 9a); the detail is the account id, never the alias text.</summary>
    public const string AliasSaved = "alias_saved";

    /// <summary>An alias was deleted in the wallet (milestone 9a); the detail is the account id, never the alias text.</summary>
    public const string AliasDeleted = "alias_deleted";
}
