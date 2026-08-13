namespace DeepDroidChanger.Services;

internal sealed class DeviceViewerActiveTaskTracker
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _activeTasks = new();

    internal int Count
    {
        get
        {
            lock (_gate)
                return _activeTasks.Count;
        }
    }

    internal Task Track(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_gate)
            _activeTasks.Add(task);

        _ = task.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
                lock (_gate)
                    _activeTasks.Remove(completedTask);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return task;
    }

    internal Task[] Snapshot()
    {
        lock (_gate)
            return _activeTasks.ToArray();
    }
}
