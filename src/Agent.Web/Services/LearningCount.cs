using Agent.Core.Learning;

namespace Agent.Web.Services;

/// <summary>
/// The number of learning-log rows not yet marked reviewed, for the side menu's count on Learning data (milestone 10).
/// A singleton: each read goes through a fresh scope's <see cref="LearningLog"/>, and <see cref="NotifyChanged"/> tells
/// every circuit's layout to read again after a tick. Handlers are called on the raiser's thread, which may be another
/// circuit's: a component marshals with <c>InvokeAsync</c>.
/// </summary>
public sealed class LearningCount(IServiceScopeFactory scopes)
{
    /// <summary>Raised by <see cref="NotifyChanged"/>; handlers must not block and may be called on any thread.</summary>
    public event Action? Changed;

    /// <summary>Reads the number of rows whose Reviewed tick is off.</summary>
    public async Task<int> ReadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LearningLog>().CountUnreviewedAsync(cancellationToken);
    }

    /// <summary>Tells every subscriber that a row's Reviewed tick changed.</summary>
    public void NotifyChanged() => Changed?.Invoke();
}
