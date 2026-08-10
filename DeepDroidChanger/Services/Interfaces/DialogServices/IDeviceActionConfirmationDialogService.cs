using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceActionConfirmationDialogService
{
    Task<bool> ConfirmDeleteDeviceAsync(
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken);

    Task<bool> ConfirmChangeAndWipeAsync(
        string deviceName,
        string deviceSerial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken);

    Task<bool> ConfirmMultipleAsync(
        MultipleDeviceBatchAction action,
        int deviceCount,
        CancellationToken cancellationToken);

    Task<bool> ConfirmChangeWithoutWipeAsync(
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken);

    Task<bool> ConfirmWipeWithoutChangeAsync(
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken);

    Task<bool> ConfirmChangeSimAsync(
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken);
}
