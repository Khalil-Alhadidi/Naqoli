namespace Agent.Web.Services;

/// <summary>What changed in the wallet: money moved, an alias was saved or deleted, or the wallet was reset to the seed.</summary>
public enum WalletChangeKind
{
    Payment,
    Alias,
    Reset,
}

/// <summary>One wallet change for <paramref name="UserId"/>; user 0 means every user (the console's reset).</summary>
public sealed record WalletChange(int UserId, WalletChangeKind Kind)
{
    /// <summary>The user id that stands for every user.</summary>
    public const int EveryUser = 0;

    /// <summary>True when the change concerns <paramref name="userId"/>: it is that user's, or every user's.</summary>
    public bool Concerns(int userId) => UserId == EveryUser || UserId == userId;
}

/// <summary>
/// The process-wide wallet-change notification (singleton, milestone 7 post-tag; Khalil, 18 Oct 2026). Every circuit's
/// <see cref="ChatSession"/> raises it when its conversation changed the wallet, and the console's side door
/// (<see cref="WalletFaults"/>) when an external debit went through or the wallet was reset; the console panels that
/// mirror wallet state (wallet data, aliases, log) subscribe, so a change made in one tab reaches every tab. Handlers
/// are called on the raiser's thread, which may be another circuit's: a component marshals with <c>InvokeAsync</c>.
/// </summary>
public sealed partial class WalletChanges(ILogger<WalletChanges> logger)
{
    /// <summary>Raised for every change; handlers must not block and may be called on any thread.</summary>
    public event Action<WalletChange>? Changed;

    /// <summary>
    /// Calls every handler with <paramref name="change"/>. A handler that throws is logged at Warning (the exception's
    /// type only, never its message) and does not stop the next handler or reach the caller.
    /// </summary>
    public void Raise(WalletChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        // A snapshot of the subscribers: the field-like event's add and remove are thread-safe, and a handler that
        // unsubscribes during the call does not change this list.
        if (Changed is not { } handlers)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action<WalletChange>>())
        {
            try
            {
                handler(change);
            }
            catch (Exception ex)
            {
                LogHandlerFailed(logger, change.Kind, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A wallet-change handler for {Kind} threw {ExceptionType}.")]
    private static partial void LogHandlerFailed(ILogger logger, WalletChangeKind kind, string exceptionType);
}
