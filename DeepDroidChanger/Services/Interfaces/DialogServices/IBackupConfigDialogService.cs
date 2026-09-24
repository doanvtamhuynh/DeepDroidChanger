using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IBackupConfigDialogService
{
    Task<DeviceBackupOptions?> ShowBackupConfigAsync(CancellationToken cancellationToken);
}
