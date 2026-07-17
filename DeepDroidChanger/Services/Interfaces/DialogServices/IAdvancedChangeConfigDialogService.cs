using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IAdvancedChangeConfigDialogService
{
    Task<DeviceChangeOptions?> ShowAdvancedChangeConfigAsync(
        string deviceSerial,
        DeviceChangeOptions currentOptions,
        CancellationToken cancellationToken);
}
