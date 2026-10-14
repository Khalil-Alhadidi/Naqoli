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
}
