namespace DeepDroidChanger.Services;

internal sealed class DeviceViewerRegistry<TEntry>
    where TEntry : class
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _serialGates = new(StringComparer.OrdinalIgnoreCase);

    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public async Task<TEntry> GetOrCreateAsync(
        string serial,
        Func<TEntry, bool> isLive,
        Func<Task<TEntry>> createAsync,
        Func<TEntry, Task> activateAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(isLive);
        ArgumentNullException.ThrowIfNull(createAsync);
        ArgumentNullException.ThrowIfNull(activateAsync);

        SemaphoreSlim serialGate;
        lock (_gate)
        {
            if (!_serialGates.TryGetValue(serial, out var existingSerialGate) || existingSerialGate is null)
            {
                serialGate = new SemaphoreSlim(1, 1);
                _serialGates.Add(serial, serialGate);
            }
            else
            {
                serialGate = existingSerialGate;
            }
        }

        await serialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TEntry? existing;
            lock (_gate)
                _entries.TryGetValue(serial, out existing);

            if (existing != null && isLive(existing))
            {
                await activateAsync(existing).ConfigureAwait(false);
                return existing;
            }

            if (existing != null)
                Remove(serial, existing);

            var created = await createAsync().ConfigureAwait(false);
            lock (_gate)
            {
                // Creation can show the window and synchronously trigger CloseAsync
                // before the async factory returns. Never resurrect that entry.
                if (isLive(created))
                    _entries[serial] = created;
            }
            return created;
        }
        finally
        {
            serialGate.Release();
        }
    }

    public bool Remove(string serial, TEntry entry)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(serial, out var existing) || !ReferenceEquals(existing, entry))
                return false;

            _entries.Remove(serial);
            return true;
        }
    }

    internal bool Contains(string serial, TEntry entry)
    {
        lock (_gate)
            return _entries.TryGetValue(serial, out var existing) && ReferenceEquals(existing, entry);
    }
}
