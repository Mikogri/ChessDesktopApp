using ChessDesktopApp.Engine;

namespace ChessDesktopApp.Services;

public sealed class EngineUsageCoordinator
{
    private readonly object _sync = new();
    private readonly HashSet<Guid> _busy = [];
    private TaskCompletionSource _changed = NewSignal();

    public bool IsBusy(Guid engineId)
    {
        lock (_sync) return _busy.Contains(engineId);
    }

    public async Task<IAsyncDisposable> AcquireAsync(
        IEnumerable<Guid> engineIds,
        bool waitForAvailability,
        CancellationToken cancellationToken = default)
    {
        var ids = engineIds.Distinct().ToArray();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task waitTask;
            lock (_sync)
            {
                if (ids.All(id => !_busy.Contains(id)))
                {
                    foreach (var id in ids) _busy.Add(id);
                    return new Lease(this, ids);
                }
                if (!waitForAvailability)
                    throw new ChessEngineException("Wybrany silnik jest obecnie zajęty przez inną partię.");
                waitTask = _changed.Task;
            }
            await waitTask.WaitAsync(cancellationToken);
        }
    }

    private void Release(IEnumerable<Guid> ids)
    {
        lock (_sync)
        {
            foreach (var id in ids) _busy.Remove(id);
            var signal = _changed;
            _changed = NewSignal();
            signal.TrySetResult();
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Lease(EngineUsageCoordinator owner, Guid[] ids) : IAsyncDisposable
    {
        private int _released;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(ids);
            }
            return ValueTask.CompletedTask;
        }
    }
}
