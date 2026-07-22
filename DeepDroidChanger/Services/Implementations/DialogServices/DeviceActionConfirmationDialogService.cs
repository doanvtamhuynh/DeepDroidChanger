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
            "DeviceManager_ConfirmChangeWithoutWipeTitle",
            "DeviceManager_ConfirmChangeWithoutWipeMessage",
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
            "DeviceManager_ConfirmWipeWithoutChangeTitle",
            "DeviceManager_ConfirmWipeWithoutChangeMessage",
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
            "DeviceManager_ConfirmChangeSimTitle",
            "DeviceManager_ConfirmChangeSimMessage",
            deviceName,
            deviceSerial,
            cancellationToken);
    }

    private async Task<bool> ShowConfirmationAsync(
        string titleKey,
        string messageKey,
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string message = string.Format(
            _localizationService.GetString(messageKey),
            deviceName,
            deviceSerial);
        bool confirmed = await _confirmationDialogService
            .ShowWarningConfirmationAsync(
                message,
                _localizationService.GetString(titleKey),
                cancellationToken)
            .ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        return confirmed;
    }
}
