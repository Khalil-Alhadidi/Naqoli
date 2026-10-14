using System.ComponentModel.DataAnnotations;
using Wallet.MockApi.Data.Seed;

namespace Wallet.MockApi;

/// <summary>
/// Fees, thresholds, attempts and timers of the mock wallet, bound from the <c>Wallet</c> configuration section.
/// Amounts are whole Iraqi dinars.
/// </summary>
public sealed class WalletOptions
{
    public const string SectionName = "Wallet";

    /// <summary>
    /// The longest delay a fault may hold a response, in milliseconds. Shared by <see cref="FaultDelayMs"/>,
    /// the fault switch and <c>POST /admin/faults</c>.
    /// </summary>
    public const int MaxFaultDelayMs = 600_000;

    /// <summary>The shortest <see cref="AdminKey"/> accepted.</summary>
    public const int MinAdminKeyLength = 16;

    [Range(typeof(long), "0", "1000000", ParseLimitsInInvariantCulture = true)]
    public long TransferFeeIqd { get; set; } = 1000;

    [Range(typeof(long), "0", "1000000", ParseLimitsInInvariantCulture = true)]
    public long BillFeeIqd { get; set; } = 500;

    [Range(typeof(long), "0", "1000000", ParseLimitsInInvariantCulture = true)]
    public long PurchaseFeeIqd { get; set; } = 0;

    /// <summary>Transactions with a total above this amount need the PIN (D8).</summary>
    [Range(typeof(long), "0", "100000000", ParseLimitsInInvariantCulture = true)]
    public long PinRequiredAboveIqd { get; set; } = 1_000_000;

    /// <summary>Wrong PIN entries allowed before the PIN is locked (D8).</summary>
    [Range(1, 10)]
    public int MaxPinAttempts { get; set; } = 3;

    /// <summary>How long an injected slow or timeout fault holds the response, in milliseconds.</summary>
    [Range(0, MaxFaultDelayMs)]
    public int FaultDelayMs { get; set; } = 60_000;

    /// <summary>
    /// The key for the admin endpoints, at least <see cref="MinAdminKeyLength"/> characters. Never stored in the
    /// repository; set it in user secrets or the environment. Unset or empty means no key (an empty value is turned
    /// into null before validation, in <c>Program.cs</c>).
    /// </summary>
    [MinLength(MinAdminKeyLength)]
    public string? AdminKey { get; set; }

    /// <summary>
    /// The directory holding the eight seed files (see <c>data/README.md</c>). Defaults to the copy of
    /// <c>data/seed</c> in the build output.
    /// </summary>
    [Required]
    public string SeedDirectory { get; set; } = SeedFiles.DefaultDirectory;
}
