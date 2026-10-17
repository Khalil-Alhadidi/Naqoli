namespace Agent.Web.Services;

/// <summary>The component the side menu shows in the middle of layout B (D17).</summary>
public enum ConsolePanel
{
    WalletData,
    Settings,
    Log,
    LearningData,

    /// <summary>The demo user's aliases (milestone 9a).</summary>
    Aliases,
}

/// <summary>The side menu's selection for one circuit, shared by the layout and the page.</summary>
public sealed class ConsoleNavigation
{
    public ConsolePanel Selected { get; private set; } = ConsolePanel.WalletData;

    public event Action? Changed;

    public void Select(ConsolePanel panel)
    {
        Selected = panel;
        Changed?.Invoke();
    }
}
