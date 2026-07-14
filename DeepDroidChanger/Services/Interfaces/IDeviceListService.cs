using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceListService
{
    Task<DeviceListSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredDeviceConfig>> LoadStoredDevicesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AdbDevice>> LoadDetectedDevicesAsync(CancellationToken cancellationToken);

    Task<DeviceListSnapshot> AddSelectedDevicesAsync(
        IEnumerable<StoredDeviceConfig> selectedDevices,
        CancellationToken cancellationToken);

    Task<DeviceDeleteResult> DeleteSavedDeviceAsync(string serial, CancellationToken cancellationToken);

    int CountNewDevices(
        IReadOnlyList<StoredDeviceConfig> storedDevices,
        IReadOnlyList<AdbDevice> connectedDevices);
}
