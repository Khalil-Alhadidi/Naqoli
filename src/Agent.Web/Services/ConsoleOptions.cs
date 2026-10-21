using System.ComponentModel.DataAnnotations;

namespace Agent.Web.Services;

/// <summary>
/// The console's own settings (section <c>Console</c>), bound and validated at start. <see cref="WalletAdminKey"/> is
/// the <c>X-Wallet-Admin-Key</c> the fault panel sends to the wallet's <c>/admin/faults</c> and <c>/admin/debit</c>. The
/// default <c>dev</c> is not a secret: the wallet accepts any non-empty value in Development when it has no admin key.
/// Outside Development the console refuses to start unless the wallet's key is set (at least 16 characters, not
/// <c>dev</c>) in user secrets or the environment (review R2 of milestone 8); it shows it as <c>set</c>, never its value.
/// </summary>
public sealed class ConsoleOptions
{
    public const string SectionName = "Console";

    /// <summary>The Development default: not a secret, and refused at start in any other environment.</summary>
    public const string DevelopmentWalletAdminKey = "dev";

    /// <summary>The shortest key outside Development: the wallet's own minimum (<c>WalletOptions.MinAdminKeyLength</c>, 16).</summary>
    public const int MinWalletAdminKeyLength = 16;

    /// <summary>The start-up message outside Development; it names the setting and never a value.</summary>
    public const string KeyRequiredMessage =
        "Console:WalletAdminKey is required outside Development: set the wallet's admin key (at least 16 characters, not the Development placeholder) in user secrets or the environment (Console__WalletAdminKey).";

    /// <summary>The key the fault panel sends as <c>X-Wallet-Admin-Key</c>.</summary>
    [Required]
    public string WalletAdminKey { get; set; } = DevelopmentWalletAdminKey;

    /// <summary>
    /// How many rows every console grid (contacts, bills, history, log, aliases) shows on one page, 5 to 100, default 10.
    /// It splits what a panel has already loaded into pages; it is not <c>Agent:LogPageSize</c> or
    /// <c>Agent:HistoryPageSize</c>, which say how much is loaded. Nothing in <c>Agent.Core</c> reads it. It stays 10 or
    /// more because three journeys count every row of U1's aliases and orders on the first page.
    /// </summary>
    [Range(5, 100)]
    public int GridPageSize { get; set; } = 10;

    /// <summary>True when <paramref name="key"/> may be used outside Development: set, not the placeholder, long enough.</summary>
    public static bool IsUsableOutsideDevelopment(string? key)
        => !string.IsNullOrWhiteSpace(key)
            && !string.Equals(key, DevelopmentWalletAdminKey, StringComparison.Ordinal)
            && key.Length >= MinWalletAdminKeyLength;
}
