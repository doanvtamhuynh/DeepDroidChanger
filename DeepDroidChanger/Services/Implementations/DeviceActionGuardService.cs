namespace DeepDroidChanger.Services;

/// <summary>
/// Thread-safe implementation of <see cref="IDeviceActionGuardService"/> guarding concurrent device actions.
/// </summary>
public sealed class DeviceActionGuardService : IDeviceActionGuardService
{
    private readonly object _syncRoot = new();
    private readonly HashSet<string> _busySerials = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event Action<string, bool>? BusyStateChanged;

    /// <inheritdoc />
    public bool IsBusy(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return false;

        lock (_syncRoot)
        {
            return _busySerials.Contains(serial.Trim());
        }
    }

    /// <inheritdoc />
    public IDisposable? TryAcquire(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string normalizedSerial = serial.Trim();

        lock (_syncRoot)
        {
            if (!_busySerials.Add(normalizedSerial))
                return null;
        }

        BusyStateChanged?.Invoke(normalizedSerial, true);
        return new DeviceActionLease(this, normalizedSerial);
    }

    private void Release(string serial)
    {
        bool removed;
        lock (_syncRoot)
        {
            removed = _busySerials.Remove(serial);
        }

        if (removed)
        {
            BusyStateChanged?.Invoke(serial, false);
        }
    }

    private sealed class DeviceActionLease : IDisposable
    {
        private DeviceActionGuardService? _owner;
        private readonly string _serial;

        public DeviceActionLease(DeviceActionGuardService owner, string serial)
        {
            _owner = owner;
            _serial = serial;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_serial);
        }
    }
}

