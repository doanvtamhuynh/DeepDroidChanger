using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IAdvancedChangeConfigDialogService
{
    Task<AdvancedChangeConfigDialogResult?> ShowAdvancedChangeConfigAsync(
        string deviceSerial,
        DeviceChangeOptions currentOptions,
        bool useIntegritySecurityPatch,
        CancellationToken cancellationToken);

    async Task<AdvancedChangeConfigDialogResult?> ShowAdvancedChangeConfigAsync(
        IReadOnlyList<string> deviceSerials,
        DeviceChangeOptions currentOptions,
        bool useIntegritySecurityPatch,
        bool isMultiple,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceSerials);
        string serial = deviceSerials.FirstOrDefault()
            ?? throw new ArgumentException("At least one device serial is required.", nameof(deviceSerials));
        return await ShowAdvancedChangeConfigAsync(
            serial,
            currentOptions,
            useIntegritySecurityPatch,
            cancellationToken).ConfigureAwait(false);
    }
}
