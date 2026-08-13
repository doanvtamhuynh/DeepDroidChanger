using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IAdbDeviceTrackerService
{
    event EventHandler<AdbDeviceStateChangedEventArgs>? DeviceStateChanged;

    event EventHandler<AdbDeviceTrackerHealthChangedEventArgs>? HealthChanged;

    AdbDeviceTrackerHealth Health { get; }

    IReadOnlyList<AdbDevice> CurrentSnapshot { get; }

    AdbDevice? GetDevice(string serial);

    Task StartAsync(CancellationToken cancellationToken);
}
