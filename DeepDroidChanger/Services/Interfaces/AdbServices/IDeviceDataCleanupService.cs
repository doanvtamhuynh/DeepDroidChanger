using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceDataCleanupService
{
    Task CleanAsync(
        string serial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken);
}
