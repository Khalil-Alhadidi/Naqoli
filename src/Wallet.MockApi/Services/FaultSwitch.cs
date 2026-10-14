using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Wallet.MockApi.Contracts;

namespace Wallet.MockApi.Services;

/// <summary>The failures a judge can inject into <c>POST /transactions</c> (architecture, section 7).</summary>
public enum FaultMode
{
    /// <summary>No fault; payments run normally.</summary>
    None,

    /// <summary>500 <c>injected_error</c> before anything is written.</summary>
    Error,

    /// <summary>Waits <see cref="FaultSetting.DelayMs"/>, then runs normally.</summary>
    Slow,

    /// <summary>Waits, then 504 <c>injected_timeout</c>; nothing is written.</summary>
    TimeoutBeforeExecute,

    /// <summary>Runs and commits, then waits before answering, so the caller times out not knowing the outcome.</summary>
    TimeoutAfterExecute,
}

/// <summary>One state of the fault switch. Immutable, so it is swapped whole.</summary>
public sealed record FaultSetting(FaultMode Mode, int DelayMs, bool Once)
{
    public FaultState ToState() => new(FaultModes.Name(Mode), DelayMs, Once);
}

/// <summary>The problem codes the injected faults return.</summary>
public static class FaultErrors
{
    public const string InjectedError = "injected_error";
    public const string InjectedTimeout = "injected_timeout";
}

/// <summary>The camelCase names fault modes carry in JSON.</summary>
public static class FaultModes
{
    private static readonly Dictionary<FaultMode, string> Names = new()
    {
        [FaultMode.None] = "none",
        [FaultMode.Error] = "error",
        [FaultMode.Slow] = "slow",
        [FaultMode.TimeoutBeforeExecute] = "timeoutBeforeExecute",
        [FaultMode.TimeoutAfterExecute] = "timeoutAfterExecute",
    };

    public static string Name(FaultMode mode) => Names[mode];

    /// <summary>Parses a mode name, ignoring case. Numbers and unknown names are refused.</summary>
    public static bool TryParse([NotNullWhen(true)] string? name, out FaultMode mode)
    {
        foreach (var (candidate, candidateName) in Names)
        {
            if (string.Equals(name, candidateName, StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }

        mode = FaultMode.None;
        return false;
    }
}

/// <summary>
/// The process-wide fault switch. A singleton; the current <see cref="FaultSetting"/> is replaced whole under a lock,
/// so <see cref="Take"/> reads and, for a one-shot fault, clears it in one step: two concurrent payments never
/// both see a fault set with <c>once</c>.
/// </summary>
public sealed class FaultSwitch(IOptionsMonitor<WalletOptions> options)
{
    private readonly Lock sync = new();
    private FaultSetting? current;

    /// <summary>The current state. Before any <see cref="Set"/>, the mode is none with the configured delay.</summary>
    public FaultSetting Get()
    {
        lock (sync)
            return current ??= Cleared();
    }

    /// <summary>
    /// Sets the fault. A null <paramref name="delayMs"/> takes <see cref="WalletOptions.FaultDelayMs"/>; any other
    /// value must be 0 to <see cref="WalletOptions.MaxFaultDelayMs"/>.
    /// </summary>
    public FaultSetting Set(FaultMode mode, int? delayMs, bool once)
    {
        if (delayMs is < 0 or > WalletOptions.MaxFaultDelayMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delayMs), delayMs, $"The delay must be 0 to {WalletOptions.MaxFaultDelayMs} ms.");
        }

        var setting = new FaultSetting(mode, delayMs ?? options.CurrentValue.FaultDelayMs, once && mode != FaultMode.None);
        lock (sync)
            current = setting;
        return setting;
    }

    /// <summary>Returns the fault that applies to this payment, and clears it if it was set for one request only.</summary>
    public FaultSetting Take()
    {
        lock (sync)
        {
            var taken = current ??= Cleared();
            if (taken.Once)
                current = Cleared();
            return taken;
        }
    }

    private FaultSetting Cleared() => new(FaultMode.None, options.CurrentValue.FaultDelayMs, false);
}
