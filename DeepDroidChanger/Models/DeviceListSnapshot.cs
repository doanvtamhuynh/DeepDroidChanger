
namespace DeepDroidChanger.Models
{
    public sealed record DeviceListSnapshot(
        IReadOnlyList<StoredDeviceConfig> StoredDevices,
        IReadOnlyList<AdbDevice> ConnectedDevices);
}
