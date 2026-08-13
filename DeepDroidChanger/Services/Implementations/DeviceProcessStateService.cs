using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

/// <summary>
/// Thread-safe, serial-scoped store for transient process/log presentation state.
/// </summary>
public sealed class DeviceProcessStateService : IDeviceProcessStateService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DeviceProcessSnapshot> _processBySerial =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DeviceProcessSnapshot> _pendingNotifications = new();
    private bool _isPublishingNotifications;

    public event Action<DeviceProcessSnapshot>? ProcessChanged;

    public DeviceProcessSnapshot? Get(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return null;

        lock (_syncRoot)
        {
            return _processBySerial.TryGetValue(NormalizeSerial(serial), out DeviceProcessSnapshot? snapshot)
                ? snapshot
                : null;
        }
    }

    public void SetProcess(string serial, string message, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        string normalizedSerial = NormalizeSerial(serial);
        DeviceProcessState nextState = GetProcessState(resourceKey);
        bool shouldPublish;

        lock (_syncRoot)
        {
            if (nextState == DeviceProcessState.Ready
                && _processBySerial.TryGetValue(normalizedSerial, out DeviceProcessSnapshot? current)
                && current.State is DeviceProcessState.Succeeded
                    or DeviceProcessState.Failed
                    or DeviceProcessState.Canceled)
            {
                return;
            }

            var snapshot = new DeviceProcessSnapshot(
                normalizedSerial,
                message,
                resourceKey,
                nextState);
            _processBySerial[normalizedSerial] = snapshot;
            shouldPublish = EnqueueNotificationLocked(snapshot);
        }

        PublishPendingNotifications(shouldPublish);
    }

    private bool EnqueueNotificationLocked(DeviceProcessSnapshot snapshot)
    {
        _pendingNotifications.Enqueue(snapshot);
        if (_isPublishingNotifications)
            return false;

        _isPublishingNotifications = true;
        return true;
    }

    private void PublishPendingNotifications(bool shouldPublish)
    {
        if (!shouldPublish)
            return;

        while (true)
        {
            DeviceProcessSnapshot snapshot;
            lock (_syncRoot)
            {
                if (_pendingNotifications.Count == 0)
                {
                    _isPublishingNotifications = false;
                    return;
                }

                snapshot = _pendingNotifications.Dequeue();
            }

            Delegate[] subscribers = ProcessChanged?.GetInvocationList() ?? [];
            foreach (Action<DeviceProcessSnapshot> subscriber in subscribers.Cast<Action<DeviceProcessSnapshot>>())
            {
                try
                {
                    subscriber(snapshot);
                }
                catch
                {
                    // A presentation subscriber must not break shared process state or notification draining.
                }
            }
        }
    }

    private static DeviceProcessState GetProcessState(string resourceKey)
    {
        if (string.Equals(resourceKey, "Log_Ready", StringComparison.Ordinal))
            return DeviceProcessState.Ready;

        if (resourceKey.Contains("Partial", StringComparison.Ordinal))
            return DeviceProcessState.Failed;

        if (resourceKey.Contains("Canceled", StringComparison.Ordinal))
            return DeviceProcessState.Canceled;

        if (resourceKey.Contains("Success", StringComparison.Ordinal)
            || resourceKey.EndsWith("Enabled", StringComparison.Ordinal)
            || resourceKey.EndsWith("Disabled", StringComparison.Ordinal)
            || resourceKey.EndsWith("Sent", StringComparison.Ordinal)
            || resourceKey.EndsWith("Saved", StringComparison.Ordinal)
            || resourceKey.EndsWith("NoOutput", StringComparison.Ordinal)
            || resourceKey.EndsWith("CompleteFormat", StringComparison.Ordinal))
        {
            return DeviceProcessState.Succeeded;
        }

        if (resourceKey.Contains("Failed", StringComparison.Ordinal)
            || resourceKey.Contains("Failure", StringComparison.Ordinal)
            || resourceKey.EndsWith("Required", StringComparison.Ordinal)
            || resourceKey.EndsWith("DeviceMustBeOnline", StringComparison.Ordinal)
            || resourceKey.EndsWith("NoFiles", StringComparison.Ordinal)
            || resourceKey.EndsWith("NoInternet", StringComparison.Ordinal)
            || resourceKey.EndsWith("Empty", StringComparison.Ordinal)
            || resourceKey.EndsWith("AlreadyExists", StringComparison.Ordinal)
            || resourceKey.EndsWith("VersionDowngrade", StringComparison.Ordinal)
            || resourceKey.EndsWith("UnknownResult", StringComparison.Ordinal)
            || resourceKey.Contains("Missing", StringComparison.Ordinal)
            || resourceKey.Contains("Invalid", StringComparison.Ordinal)
            || resourceKey.Contains("Unsupported", StringComparison.Ordinal)
            || resourceKey.Contains("Insufficient", StringComparison.Ordinal)
            || resourceKey.Contains("NoMatching", StringComparison.Ordinal))
        {
            return DeviceProcessState.Failed;
        }

        return DeviceProcessState.InProgress;
    }

    private static string NormalizeSerial(string serial)
    {
        return serial.Trim();
    }
}
