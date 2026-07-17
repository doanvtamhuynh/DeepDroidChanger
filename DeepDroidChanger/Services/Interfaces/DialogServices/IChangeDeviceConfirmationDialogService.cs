using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IChangeDeviceConfirmationDialogService
{
    Task<bool> ShowChangeDeviceConfirmationAsync(
        string deviceName,
        string deviceSerial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken);
}
