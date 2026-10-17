using System.ComponentModel.DataAnnotations;

namespace Agent.Core;

/// <summary>
/// The agent's limits, timers and wallet connection, bound from the <c>Agent</c> section. The defaults of the limits
/// are the test set's settings (<c>data/seed/settings.json</c>); <c>AgentOptionsTests</c> keeps them equal.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>The shortest wallet client key accepted, as the wallet requires of its own key.</summary>
    public const int MinWalletClientKeyLength = 16;

    /// <summary>The key the client sends in Development when none is configured; the wallet accepts any value there.</summary>
    public const string DevelopmentWalletClientKey = "dev";

    /// <summary>The smallest transfer amount, in IQD.</summary>
    [Range(typeof(long), "1", "1000000000000")]
    public long MinAmountIqd { get; set; } = 1_000;

    /// <summary>The largest amount of one payment, without the fee, in IQD.</summary>
    [Range(typeof(long), "1", "1000000000000")]
    public long PerTransactionCapIqd { get; set; } = 5_000_000;

    /// <summary>The largest amount paid in one Iraq day, without fees, in IQD.</summary>
    [Range(typeof(long), "1", "1000000000000")]
    public long PerDayCapIqd { get; set; } = 10_000_000;

    /// <summary>How long a confirmation card stays open.</summary>
    [Range(1, 3_600)]
    public int CardTimerSeconds { get; set; } = 45;

    /// <summary>The most requests one message may carry.</summary>
    [Range(1, 20)]
    public int MaxRequestsPerMessage { get; set; } = 3;

    /// <summary>The most products one question may offer.</summary>
    [Range(1, 50)]
    public int MaxProductsInOneQuestion { get; set; } = 8;

    /// <summary>A payment like one sent within this window gets a repeat warning.</summary>
    [Range(0, 1_440)]
    public int RepeatWarningWindowMinutes { get; set; } = 10;

    /// <summary>How far back the history the agent reads goes.</summary>
    [Range(1, 3_650)]
    public int HistoryDays { get; set; } = 90;

    /// <summary>The mock wallet's address.</summary>
    [Required]
    public Uri? WalletBaseUrl { get; set; }

    /// <summary>
    /// The value of <c>X-Wallet-Client-Key</c>. From user secrets or the environment, never from a committed file. Null
    /// (or empty) sends <see cref="DevelopmentWalletClientKey"/> in Development and is refused at start elsewhere.
    /// </summary>
    [MinLength(MinWalletClientKeyLength)]
    public string? WalletClientKey { get; set; }

    /// <summary>How long one wallet call may take before its outcome counts as unknown.</summary>
    [Range(1, 300)]
    public int WalletTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// How long after a card was created a settle may still treat a PIN refusal (<c>pin_required</c>,
    /// <c>pin_invalid</c>, <c>pin_malformed</c>, <c>pin_locked</c>) as "the original call may still be running" and
    /// leave the record Checking. The default, 610, is the wallet timeout (10 s) plus the wallet's longest fault delay
    /// (600 s): past it the original call can no longer be running, so such a refusal finalises the record NotDone.
    /// </summary>
    [Range(1, 86_400)]
    public int SettleWindowSeconds { get; set; } = 610;

    /// <summary>
    /// While a card is Checking, the phone settles it by itself every this many seconds (the same settle as the "check
    /// again" button, under the same key; rule 6), until the record leaves Checking. Milestone 8; editable live.
    /// </summary>
    [Range(1, 600)]
    public int CheckingRetrySeconds { get; set; } = 5;

    /// <summary>The most history items read in one call; the wallet clamps <c>take</c> to 200.</summary>
    [Range(1, 200)]
    public int HistoryPageSize { get; set; } = 200;

    /// <summary>The wallet user the demo console acts for (U1, the everyday user).</summary>
    [Range(1, int.MaxValue)]
    public int DemoUserId { get; set; } = 1;

    /// <summary>
    /// How often the hosted <see cref="Pending.PendingSweeper"/> expires cards past their timer and moves stale Executing
    /// records to Checking. 0 registers no sweeper (the test hosts sweep by hand).
    /// </summary>
    [Range(0, 3_600)]
    public int SweepIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// How long a record may stay Executing before the sweep moves it to Checking. A Confirm makes three timed wallet
    /// calls while the record is Executing (balance, history, execute), so this must be more than three times
    /// <see cref="WalletTimeoutSeconds"/>; the default, 40, is 3 x 10 s plus a margin.
    /// </summary>
    [Range(1, 86_400)]
    public int StaleExecutingAfterSeconds { get; set; } = 40;

    /// <summary>
    /// Matching rule 4 (Khalil, 14 Oct 2026): a typed word whose key is shorter than this must equal a whole word of a
    /// saved name or alias; longer words may match inside one.
    /// </summary>
    [Range(1, 20)]
    public int MinSubstringWordLength { get; set; } = Resolver.ContactResolver.MinSubstringWordLength;

    /// <summary>
    /// D6 (Khalil, 15 Oct 2026): a message is a yes, or a no, when it is made only of whole yes (no) entries and it has
    /// at most this many words (words, not entries). Never a substring match.
    /// </summary>
    [Range(1, 20)]
    public int MaxYesNoWords { get; set; } = 4;

    /// <summary>How many audit rows, newest first, the console's log component shows.</summary>
    [Range(1, 1_000)]
    public int LogPageSize { get; set; } = 100;

    /// <summary>
    /// D6: the typed words that mean "yes" while a card is live. A yes never confirms; it points to the Confirm button.
    /// Each entry is a phrase: its words, normalised as a message is (<see cref="Text.ArabicText.Normalize"/>,
    /// punctuation and symbols dropped, Latin lower-cased), kept in order. A message of at most
    /// <see cref="MaxYesNoWords"/> words is a yes when its words can be cut, left to right, into whole entries (the
    /// longest entry that fits at each position); one word of a two-word entry on its own matches nothing. Empty by default: <c>Agent.Core</c> holds no phone text (rule 4),
    /// so the web console sets them from its resource file and a test sets its own.
    /// </summary>
    public string[] YesWords { get; set; } = [];

    /// <summary>D6: the typed words that mean "no" while a card is live; a no cancels the card. See <see cref="YesWords"/>.</summary>
    public string[] NoWords { get; set; } = [];

    /// <summary>
    /// The bill resolver's provider spellings, by biller code (Khalil, 16 Oct 2026): besides the first word of the
    /// wallet's biller name, the spellings users type (taken from the development set and the matching rules),
    /// compared as contact names are. Configuration, not code: <c>Agent.Core</c> holds no Arabic. Empty by default;
    /// shown read only in the settings component.
    /// </summary>
    public Dictionary<string, List<string>> ProviderSpellings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Matching rule 1, endearments (Khalil, 17 Oct 2026): words of affection a user adds to a name. The contact resolver
    /// matches the full typed text exactly first, so an alias that itself contains one of these words wins untouched;
    /// only when nothing matches exactly are these words dropped and the matching run again. Configuration, not code:
    /// <c>Agent.Core</c> holds no Arabic. Empty by default; shown read only (as a count) in the settings component.
    /// </summary>
    public string[] Endearments { get; set; } = [];

    /// <summary>
    /// The parse call: <c>scripted</c> (the default; no model) or <c>claude</c> (the <c>Model</c> section's candidate,
    /// with a key in user secrets). Read by <see cref="Parsing.ParserFactory.AddAgentParser"/>.
    /// </summary>
    [RegularExpression("^(scripted|claude)$")]
    public string Parser { get; set; } = Parsing.ParserFactory.Scripted;

    /// <summary>The default of <see cref="DevSetPath"/>.</summary>
    public const string DefaultDevSetPath = "data/" + Parsing.ScriptedParser.DevSetFileName;

    /// <summary>
    /// The development set the scripted parser loads (with its sidecar of parses next to it), relative to the repository
    /// root unless rooted. Its file name must be <c>requests.dev.jsonl</c> (rule 7). Read by
    /// <see cref="Parsing.ParserFactory.AddAgentParser"/>; shown read-only in settings.
    /// </summary>
    [Required]
    public string DevSetPath { get; set; } = DefaultDevSetPath;

    /// <summary>True when <see cref="StaleExecutingAfterSeconds"/> outlasts the three timed wallet calls of a Confirm.</summary>
    internal bool StaleExecutingOutlastsAConfirm => StaleExecutingAfterSeconds > 3 * WalletTimeoutSeconds;

    /// <summary>True when <see cref="MinAmountIqd"/> &lt;= <see cref="PerTransactionCapIqd"/> &lt;= <see cref="PerDayCapIqd"/>.</summary>
    internal bool LimitsAreOrdered => MinAmountIqd <= PerTransactionCapIqd && PerTransactionCapIqd <= PerDayCapIqd;
}
