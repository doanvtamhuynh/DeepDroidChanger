using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceMetadataChangeNotifier
{
    event EventHandler<DeviceNameChangedEventArgs>? DeviceNameChanged;

    void PublishDeviceNameChanged(string serial, string name);
}
