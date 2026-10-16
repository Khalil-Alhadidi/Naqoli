using System.ComponentModel.DataAnnotations;

namespace Agent.Web.Services;

/// <summary>
/// The console's own settings (section <c>Console</c>), bound and validated at start. <see cref="WalletAdminKey"/> is
/// the <c>X-Wallet-Admin-Key</c> the fault panel sends to the wallet's <c>/admin/faults</c> and <c>/admin/debit</c>. The
/// default <c>dev</c> is not a secret: the wallet accepts any non-empty value in Development when it has no admin key.
/// Outside Development set the wallet's key in user secrets or the environment; the console shows it as <c>set</c>,
/// never its value.
/// </summary>
public sealed class ConsoleOptions
{
    public const string SectionName = "Console";

    /// <summary>The key the fault panel sends as <c>X-Wallet-Admin-Key</c>.</summary>
    [Required]
    public string WalletAdminKey { get; set; } = "dev";
}
