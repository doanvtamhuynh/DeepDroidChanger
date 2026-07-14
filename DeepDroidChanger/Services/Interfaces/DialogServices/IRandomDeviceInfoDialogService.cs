using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IRandomDeviceInfoDialogService
{
    Task<bool> ShowRandomDeviceInfoAsync(DeviceInfoApiDevice device, CancellationToken cancellationToken);
}
