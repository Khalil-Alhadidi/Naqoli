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

    /// <summary>The most history items read in one call; the wallet clamps <c>take</c> to 200.</summary>
    [Range(1, 200)]
    public int HistoryPageSize { get; set; } = 200;
}
