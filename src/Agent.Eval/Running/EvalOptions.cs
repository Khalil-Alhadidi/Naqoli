using System.ComponentModel.DataAnnotations;

namespace Agent.Eval.Running;

/// <summary>The runner's settings, bound from the <c>Eval</c> section and shown in the report header.</summary>
/// <remarks>
/// The runner's <c>Agent</c> section (<c>src/Agent.Eval/appsettings.json</c>) carries the D6 words in
/// <c>Agent:YesWords</c> and <c>Agent:NoWords</c>, because <c>Agent.Eval</c> cannot read the web console's resource file.
/// They must equal <c>Word_yes</c> and <c>Word_no</c> of <c>src/Agent.Web/Resources/PhoneStrings.resx</c> (a test
/// compares them) until Khalil writes the words; then both change together.
/// </remarks>
public sealed class EvalOptions
{
    public const string SectionName = "Eval";

    /// <summary>The directory of the request sets and of <c>seed/</c>, relative to the repository root unless absolute.</summary>
    [Required]
    public string SetDirectory { get; set; } = "data";

    /// <summary>Where the reports are written, relative to the repository root unless absolute.</summary>
    [Required]
    public string ReportDirectory { get; set; } = "docs/reports";

    /// <summary>
    /// The wallet's <c>X-Wallet-Admin-Key</c>. From user secrets or the environment; the default, <c>dev</c>, works only
    /// against a wallet in Development with no admin key configured.
    /// </summary>
    [Required]
    public string WalletAdminKey { get; set; } = "dev";

    /// <summary>The longest one step may take before the case is stopped with <c>timeout</c>.</summary>
    [Range(1, 3_600)]
    public int CaseTimeoutSeconds { get; set; } = 60;

    /// <summary>How many times the runner taps "check again" after a Checking outcome.</summary>
    [Range(0, 20)]
    public int SettleAttempts { get; set; } = 3;

    /// <summary>The pause before each "check again".</summary>
    [Range(0, 60_000)]
    public int SettleDelayMs { get; set; } = 200;

    /// <summary>
    /// The <c>delayMs</c> of the wallet's timeout faults (<c>wallet_timeout_*</c>). It must be above the agent's wallet
    /// timeout (<c>Agent:WalletTimeoutSeconds</c>, 10 s by default): a shorter delay only slows the call down, and the
    /// agent never sees the timeout that the fault is meant to cause (so the card would never reach Checking).
    /// </summary>
    [Range(0, 600_000)]
    public int FaultDelayMs { get; set; } = 12_000;
}
