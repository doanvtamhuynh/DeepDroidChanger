using System.Collections.Concurrent;
using DeepDroidChanger.ViewDevices.Contracts;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetDeviceLeaseCoordinator : IScrcpyNetDeviceLeaseCoordinator
{
    private readonly ConcurrentDictionary<string, LeaseToken> _leases =
        new(StringComparer.OrdinalIgnoreCase);

    public IDisposable Acquire(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        string normalizedSerial = serial.Trim();
        LeaseToken lease = new(this, normalizedSerial);
        if (!_leases.TryAdd(normalizedSerial, lease))
        {
            throw new ScrcpyNetDeviceBusyException(normalizedSerial);
        }

        return lease;
    }

    private void Release(LeaseToken lease)
    {
        if (Interlocked.Exchange(ref lease.Released, 1) != 0)
            return;

        ((ICollection<KeyValuePair<string, LeaseToken>>)_leases)
            .Remove(new KeyValuePair<string, LeaseToken>(lease.Serial, lease));
    }

    private sealed class LeaseToken(
        ScrcpyNetDeviceLeaseCoordinator owner,
        string serial) : IDisposable
    {
        public string Serial { get; } = serial;
        public int Released;

        public void Dispose()
        {
            owner.Release(this);
        }
    }
}
