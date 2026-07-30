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
