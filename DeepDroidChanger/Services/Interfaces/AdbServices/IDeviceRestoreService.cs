using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceRestoreService
{
    Task<DeviceRestoreInspection> InspectAsync(
        string archivePath,
        string password,
        CancellationToken cancellationToken);

    Task<DeviceRestoreResult> RestoreAsync(
        string serial,
        DeviceRestoreOptions options,
        IProgress<DeviceRestoreProgress>? progress,
        CancellationToken cancellationToken);
}
