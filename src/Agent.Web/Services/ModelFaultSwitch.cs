namespace Agent.Web.Services;

/// <summary>A model fault the console can inject on the parse call.</summary>
public enum ModelFaultMode
{
    /// <summary>No fault: the parse call goes to the parser.</summary>
    None,

    /// <summary>The model cannot be reached: the call throws, and the phone shows <c>model_unavailable</c>.</summary>
    Unreachable,

    /// <summary>The model's reply is not the required structure: unclear with problem <c>injected</c> (<c>model_invalid_reply</c>).</summary>
    InvalidReply,
}

/// <summary>
/// The console's model fault switch (milestone 8): one per console process, shared by every circuit, read by
/// <see cref="FaultingParser"/> on each parse call. <see cref="Take"/> reads the armed fault and, when it was set to
/// fire once, clears it, under one lock, so two calls never both take a once fault. <see cref="Changed"/> tells the
/// fault panels to show the state again.
/// </summary>
public sealed class ModelFaultSwitch
{
    private readonly Lock gate = new();
    private ModelFaultMode mode;
    private bool once;

    /// <summary>Raised after the state changes: set, cleared, or a once fault taken.</summary>
    public event Action? Changed;

    public ModelFaultMode Mode
    {
        get
        {
            lock (gate)
                return mode;
        }
    }

    public bool Once
    {
        get
        {
            lock (gate)
                return once;
        }
    }

    /// <summary>Arms <paramref name="faultMode"/>; with <paramref name="once"/> it clears after one parse call.</summary>
    public void Set(ModelFaultMode faultMode, bool once)
    {
        lock (gate)
        {
            mode = faultMode;
            this.once = faultMode != ModelFaultMode.None && once;
        }

        Changed?.Invoke();
    }

    /// <summary>No fault.</summary>
    public void Clear() => Set(ModelFaultMode.None, once: false);

    /// <summary>The fault for this parse call; a once fault is cleared as it is taken.</summary>
    public ModelFaultMode Take()
    {
        ModelFaultMode taken;
        bool cleared;
        lock (gate)
        {
            taken = mode;
            cleared = taken != ModelFaultMode.None && once;
            if (cleared)
            {
                mode = ModelFaultMode.None;
                once = false;
            }
        }

        if (cleared)
            Changed?.Invoke();
        return taken;
    }

    /// <summary>The mode's name on the panel: <c>none</c>, <c>unreachable</c> or <c>invalid_reply</c>.</summary>
    public static string NameOf(ModelFaultMode faultMode) => faultMode switch
    {
        ModelFaultMode.None => "none",
        ModelFaultMode.Unreachable => "unreachable",
        ModelFaultMode.InvalidReply => "invalid_reply",
        _ => throw new ArgumentOutOfRangeException(nameof(faultMode), faultMode, null),
    };

    /// <summary>Reads a mode's name (exact, lower case).</summary>
    public static bool TryParse(string? name, out ModelFaultMode faultMode)
    {
        switch (name)
        {
            case "none": faultMode = ModelFaultMode.None; return true;
            case "unreachable": faultMode = ModelFaultMode.Unreachable; return true;
            case "invalid_reply": faultMode = ModelFaultMode.InvalidReply; return true;
            default: faultMode = ModelFaultMode.None; return false;
        }
    }
}
