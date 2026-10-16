using Agent.Core.Conversation;
using Agent.Core.Learning;
using Agent.Core.Parsing;
using Agent.Core.Pipeline;

namespace Agent.Eval.Running;

/// <summary>What the runner saw right after one step: codes, states and figures only.</summary>
/// <param name="Index">The step's position, from 0.</param>
/// <param name="Kind">The step form: <c>say</c>, <c>tap option</c>, <c>tap confirm</c>, <c>tap cancel</c>, <c>wait expire</c>...</param>
/// <param name="CardState">The card's state after the step (and after any settles), or null with no card.</param>
/// <param name="OutcomeCode">The last outcome code of the step (<see cref="OutcomeCodes"/>), or null.</param>
/// <param name="ReplyCodes">The reason codes the agent answered with during the step: refusals, notes, unsupported reasons, <c>balance</c>, and a <c>not_done</c> outcome's reason.</param>
/// <param name="Replies">The agent's messages of the step.</param>
/// <param name="MovedIqd">The money the agent's payments took from the wallet since the case began (amounts plus fees).</param>
/// <param name="Executions">The agent's new <c>done</c> transactions since the case began.</param>
/// <param name="AttemptsLeft">The PIN attempts left of a <c>pin_wrong</c> outcome in the step.</param>
/// <param name="CardIssued">A card appeared in this step that was not there before.</param>
/// <param name="NewCard">That card replaced an earlier one.</param>
/// <param name="PinSeen">The user's PIN appears in a message, a trace step or an audit row (it must never).</param>
/// <param name="StatusSequence">The card states seen during the step, in order.</param>
/// <param name="ElapsedMs">
/// The time from the start of the case's first step to the end of this step (its settles and the external debit
/// included, the runner's observation of the step not).
/// </param>
public sealed record StepObservation(
    int Index,
    string Kind,
    string? CardState,
    string? OutcomeCode,
    IReadOnlyList<string> ReplyCodes,
    IReadOnlyList<ConversationMessage> Replies,
    long MovedIqd,
    int Executions,
    int? AttemptsLeft,
    bool CardIssued,
    bool NewCard,
    bool PinSeen,
    IReadOnlyList<string> StatusSequence,
    long ElapsedMs = 0);

/// <summary>
/// The link from a wallet transaction to the pending record that reached it: the record's ID and the wallet reference its
/// <c>done</c> audit row carries. A reference code, never a figure or a PIN.
/// </summary>
public sealed record RecordLink(Guid PendingId, string Reference);

/// <summary>A card issued during the case and the last state it reached.</summary>
public sealed record CardResult(CardView View, string? FinalState);

/// <summary>
/// Everything the runner saw while playing one case, for the grader. It holds the texts handed to the parser (for the
/// outbound check) but no report prints them; every other field is a code, a state or a figure.
/// </summary>
/// <remarks>
/// <see cref="ConfirmTaps"/> is the number of <c>tap confirm</c> steps the runner played (a <c>twice</c> step counts
/// once): the red-team pass mark counts the executions beyond them. <see cref="RecordLinks"/> are the case's <c>done</c>
/// audit rows (pending ID and wallet reference), read after the last step; null means none were read.
/// </remarks>
public sealed record CaseObservation(
    string CaseId,
    int UserId,
    IReadOnlyList<StepObservation> Steps,
    IReadOnlyList<CardResult> Cards,
    IReadOnlyList<ConversationQuestion> QuestionsAsked,
    IReadOnlyList<ConversationMessage> Replies,
    bool ConsentShown,
    LearningEntry? LearningEntry,
    IReadOnlyList<string> ParserTexts,
    ParseUsage Usage,
    long ElapsedMs,
    WalletSnapshot WalletBefore,
    WalletSnapshot WalletAfter,
    WalletDiff WalletDiff,
    string? UnsupportedStep,
    string? StepError,
    IReadOnlyList<string> StatusSequence,
    int ConfirmTaps = 0,
    IReadOnlyList<RecordLink>? RecordLinks = null)
{
    /// <summary>Every card issued, in order.</summary>
    public IReadOnlyList<CardView> CardsIssued => [.. Cards.Select(c => c.View)];

    /// <summary>The name of a <see cref="CardStatus"/> as the case files write it (<c>not_done</c>).</summary>
    public static string StateName(CardStatus status) => status switch
    {
        CardStatus.Live => "live",
        CardStatus.Done => "done",
        CardStatus.NotDone => "not_done",
        CardStatus.Checking => "checking",
        CardStatus.Expired => "expired",
        CardStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
