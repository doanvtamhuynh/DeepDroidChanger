using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceBackupService
{
    Task<DeviceBackupResult> BackupAsync(
        string serial,
        DeviceBackupOptions options,
        IProgress<DeviceBackupProgress>? progress,
        CancellationToken cancellationToken);
}