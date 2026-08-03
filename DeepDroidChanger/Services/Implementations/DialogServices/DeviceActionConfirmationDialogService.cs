using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed class DeviceActionConfirmationDialogService : IDeviceActionConfirmationDialogService
{
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ILocalizationService _localizationService;

    public DeviceActionConfirmationDialogService(
        IConfirmationDialogService confirmationDialogService,
        ILocalizationService localizationService)
    {
        _confirmationDialogService = confirmationDialogService;
        _localizationService = localizationService;
    }

    public Task<bool> ConfirmChangeWithoutWipeAsync(
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            "ChangeSingleDevice_ConfirmChangeWithoutWipeCaption",
            "ChangeSingleDevice_ConfirmChangeWithoutWipeMessage",
            "ChangeSingleDevice_ConfirmChangeWithoutWipeWarning",
            ConfirmationDialogIcon.ChangeDevice,
            deviceName,
            deviceSerial,
            cancellationToken);
    }

    public async Task<bool> ConfirmMultipleAsync(
        MultipleDeviceBatchAction action,
        int deviceCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviceCount);
        (string captionKey, string messageKey, string warningKey, ConfirmationDialogIcon icon) = action switch
        {
            MultipleDeviceBatchAction.ChangeAndWipe => (
                "ChangeMultipleDevices_ConfirmChangeAndWipeCaption",
                "ChangeMultipleDevices_ConfirmChangeAndWipeMessage",
                "ChangeMultipleDevices_ConfirmChangeAndWipeWarning",
                ConfirmationDialogIcon.ChangeDevice),
            MultipleDeviceBatchAction.ChangeWithoutWipe => (
                "ChangeMultipleDevices_ConfirmChangeWithoutWipeCaption",
                "ChangeMultipleDevices_ConfirmChangeWithoutWipeMessage",
                "ChangeMultipleDevices_ConfirmChangeWithoutWipeWarning",
                ConfirmationDialogIcon.ChangeDevice),
            MultipleDeviceBatchAction.WipeWithoutChange => (
                "ChangeMultipleDevices_ConfirmWipeWithoutChangeCaption",
                "ChangeMultipleDevices_ConfirmWipeWithoutChangeMessage",
                "ChangeMultipleDevices_ConfirmWipeWithoutChangeWarning",
                ConfirmationDialogIcon.Wipe),
            MultipleDeviceBatchAction.ChangeSim => (
                "ChangeMultipleDevices_ConfirmChangeSimCaption",
                "ChangeMultipleDevices_ConfirmChangeSimMessage",
                "ChangeMultipleDevices_ConfirmChangeSimWarning",
                ConfirmationDialogIcon.Sim),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        cancellationToken.ThrowIfCancellationRequested();
        bool confirmed = await _confirmationDialogService
            .ShowConfirmationAsync(
                new ConfirmationDialogOptions
                {
                    Caption = string.Format(_localizationService.GetString(captionKey), deviceCount),
                    Message = string.Format(_localizationService.GetString(messageKey), deviceCount),
                    WarningMessage = _localizationService.GetString(warningKey),
                    Icon = icon
                },
                cancellationToken)
            .ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        return confirmed;
    }

    public Task<bool> ConfirmWipeWithoutChangeAsync(
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            "ChangeSingleDevice_ConfirmWipeWithoutChangeCaption",
            "ChangeSingleDevice_ConfirmWipeWithoutChangeMessage",
            "ChangeSingleDevice_ConfirmWipeWithoutChangeWarning",
            ConfirmationDialogIcon.Wipe,
            deviceName,
            deviceSerial,
            cancellationToken);
    }

    public Task<bool> ConfirmChangeSimAsync(
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken)
    {
        return ShowConfirmationAsync(
            "ChangeSingleDevice_ConfirmChangeSimCaption",
            "ChangeSingleDevice_ConfirmChangeSimMessage",
            "ChangeSingleDevice_ConfirmChangeSimWarning",
            ConfirmationDialogIcon.Sim,
            deviceName,
            deviceSerial,
            cancellationToken);
    }

    private async Task<bool> ShowConfirmationAsync(
        string captionKey,
        string messageKey,
        string warningKey,
        ConfirmationDialogIcon icon,
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool confirmed = await _confirmationDialogService
            .ShowConfirmationAsync(
                new ConfirmationDialogOptions
                {
                    Caption = string.Format(
                        _localizationService.GetString(captionKey),
                        deviceName,
                        deviceSerial),
                    Message = _localizationService.GetString(messageKey),
                    WarningMessage = _localizationService.GetString(warningKey),
                    Icon = icon
                },
                cancellationToken)
            .ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        return confirmed;
    }
}
