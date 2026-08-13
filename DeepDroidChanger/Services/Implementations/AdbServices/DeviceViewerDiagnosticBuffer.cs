namespace DeepDroidChanger.Services;

/// <summary>
/// Bounded startup-only diagnostics retained for scrcpy window discovery errors.
/// </summary>
internal sealed class DeviceViewerDiagnosticBuffer
{
    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly int _capacity;

    internal DeviceViewerDiagnosticBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _lines.Count;
        }
    }

    internal bool IsEmpty => Count == 0;

    internal void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_gate)
        {
            if (_lines.Count == _capacity)
                _lines.Dequeue();

            _lines.Enqueue(line);
        }
    }

    internal string[] Snapshot()
    {
        lock (_gate)
            return _lines.ToArray();
    }
}
