namespace Wallet.MockApi.Services;

/// <summary>
/// Keeps a reset from interleaving with payments or with another reset. Payments enter shared, so they still run
/// side by side and the database guarantees (the idempotency index, the balance floor) are what keeps them apart.
/// A reset enters exclusive: it stops new payments from entering, waits for those inside to leave, and holds the
/// gate until it is done. A singleton; it guards this process only, which is the one process the mock wallet runs in.
/// </summary>
/// <remarks>
/// Callers enter before they open a database transaction and leave in a <c>finally</c> (through the returned
/// <see cref="IDisposable"/>), so the gate never waits on a lock the database holds for the same caller.
/// </remarks>
public sealed class WalletGate : IDisposable
{
    // Held briefly by each payment on its way in, and for the whole of a reset.
    private readonly SemaphoreSlim entry = new(1, 1);
    private readonly Lock sync = new();
    private int inside;
    private TaskCompletionSource? drained;

    /// <summary>Enters as a payment. Waits while a reset holds the gate.</summary>
    public async Task<IDisposable> EnterSharedAsync(CancellationToken cancellationToken)
    {
        await entry.WaitAsync(cancellationToken);
        try
        {
            lock (sync)
                inside++;
        }
        finally
        {
            entry.Release();
        }

        return new Releaser(LeaveShared);
    }

    /// <summary>Enters as a reset: alone, after every payment inside has left.</summary>
    public async Task<IDisposable> EnterExclusiveAsync(CancellationToken cancellationToken)
    {
        await entry.WaitAsync(cancellationToken);
        try
        {
            Task empty;
            lock (sync)
            {
                if (inside == 0)
                {
                    empty = Task.CompletedTask;
                }
                else
                {
                    drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    empty = drained.Task;
                }
            }

            await empty.WaitAsync(cancellationToken);
        }
        catch
        {
            entry.Release();
            throw;
        }

        return new Releaser(() => entry.Release());
    }

    public void Dispose() => entry.Dispose();

    private void LeaveShared()
    {
        lock (sync)
        {
            inside--;
            if (inside == 0 && drained is not null)
            {
                drained.TrySetResult();
                drained = null;
            }
        }
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? pending = release;

        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}
