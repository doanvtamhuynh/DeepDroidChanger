using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IAdvancedChangeConfigDialogService
{
    Task<AdvancedChangeConfigDialogResult?> ShowAdvancedChangeConfigAsync(
        string deviceSerial,
        DeviceChangeOptions currentOptions,
        bool useIntegritySecurityPatch,
        CancellationToken cancellationToken);
}
