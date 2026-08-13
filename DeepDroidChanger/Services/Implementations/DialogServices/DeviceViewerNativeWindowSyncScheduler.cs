namespace DeepDroidChanger.Services;

internal sealed class DeviceViewerNativeWindowSyncScheduler
{
    private readonly object _gate = new();
    private readonly Func<Task> _synchronizeAsync;
    private readonly Action? _workerDetached;
    private bool _dirty;
    private bool _closed;
    private TaskCompletionSource? _worker;

    public DeviceViewerNativeWindowSyncScheduler(
        Func<Task> synchronizeAsync,
        Action? workerDetached = null)
    {
        _synchronizeAsync = synchronizeAsync;
        _workerDetached = workerDetached;
    }

    public Task RequestAsync()
    {
        lock (_gate)
        {
            if (_closed)
                return Task.CompletedTask;

            _dirty = true;
            if (_worker != null)
                return _worker.Task;

            var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _worker = worker;
            _ = RunWorkerAsync(worker);
            return worker.Task;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _dirty = false;
        }
    }

    private async Task RunWorkerAsync(TaskCompletionSource worker)
    {
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_closed)
                    {
                        DetachWorker(worker);
                        worker.TrySetResult();
                        return;
                    }

                    _dirty = false;
                }

                await _synchronizeAsync().ConfigureAwait(false);

                lock (_gate)
                {
                    if (_closed || !_dirty)
                    {
                        DetachWorker(worker);
                        worker.TrySetResult();
                        return;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
                DetachWorker(worker);

            worker.TrySetException(exception);
        }
    }

    private void DetachWorker(TaskCompletionSource worker)
    {
        if (!ReferenceEquals(_worker, worker))
            return;

        _worker = null;
        _workerDetached?.Invoke();
    }
}
