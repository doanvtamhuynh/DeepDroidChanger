using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceDataCleanupService
{
    Task CleanAsync(
        string serial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken);

    Task CleanPreservingSsaidAsync(
        string serial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken);

    Task CleanPostRebootAsync(
        string serial,
        CancellationToken cancellationToken);

    Task DeleteSsaidAsync(
        string serial,
        CancellationToken cancellationToken);
}
