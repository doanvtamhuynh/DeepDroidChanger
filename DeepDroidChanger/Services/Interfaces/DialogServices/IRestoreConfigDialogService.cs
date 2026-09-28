using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IRestoreConfigDialogService
{
    Task<DeviceRestoreOptions?> ShowRestoreConfigAsync(
        string targetSerial,
        CancellationToken cancellationToken);

    Task<DeviceRestoreBatchOptions?> ShowRestoreBatchConfigAsync(
        IReadOnlyList<string> archivePaths,
        IReadOnlyList<string> targetSerials,
        CancellationToken cancellationToken);
}
