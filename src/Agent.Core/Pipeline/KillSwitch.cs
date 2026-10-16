namespace Agent.Core.Pipeline;

/// <summary>
/// The agent-side stop (Khalil, 15 Oct 2026). While it is on, no card is issued (<see cref="Orchestrator"/>), a Confirm
/// ends its card NotDone without an execute call and a settle sends nothing (<see cref="Confirm.ConfirmHandler"/>); the
/// balance question still answers. In memory only, one per application: off at every start. Each flip in the console
/// writes an audit row (<see cref="Audit.AuditEvents.KillSwitch"/>).
/// </summary>
public sealed class KillSwitch
{
    private volatile bool on;

    /// <summary>True while the switch is on.</summary>
    public bool IsOn => on;

    /// <summary>
    /// Raised after every <see cref="Set"/>, on the caller's thread, so every open console shows the true state.
    /// Handlers must not block.
    /// </summary>
    public event Action? Changed;

    /// <summary>Turns the switch on or off, then raises <see cref="Changed"/>.</summary>
    public void Set(bool on)
    {
        this.on = on;
        Changed?.Invoke();
    }
}
