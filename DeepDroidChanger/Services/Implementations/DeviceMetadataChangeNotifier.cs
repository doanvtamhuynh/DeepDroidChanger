using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed class DeviceMetadataChangeNotifier : IDeviceMetadataChangeNotifier
{
    public event EventHandler<DeviceNameChangedEventArgs>? DeviceNameChanged;

    public void PublishDeviceNameChanged(string serial, string name)
    {
        DeviceNameChanged?.Invoke(this, new DeviceNameChangedEventArgs(serial, name));
    }
}
