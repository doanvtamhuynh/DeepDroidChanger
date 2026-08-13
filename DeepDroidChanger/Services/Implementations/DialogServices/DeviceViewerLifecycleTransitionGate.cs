namespace DeepDroidChanger.Services;

/// <summary>
/// Serializes viewer lifecycle transitions and drains work that was already queued at close.
/// </summary>
internal sealed class DeviceViewerLifecycleTransitionGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _closed;

    internal async Task RunAsync(Func<Task> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        if (Volatile.Read(ref _closed) != 0)
            return;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closed) == 0)
                await transition().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal void Close()
    {
        Volatile.Write(ref _closed, 1);
    }

    internal async Task DrainAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Release();
    }
}
