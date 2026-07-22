using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class ChangeDeviceConfirmationDialogService : IChangeDeviceConfirmationDialogService
{
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<ChangeDeviceConfirmationDialogService> _logger;

    public ChangeDeviceConfirmationDialogService(
        IConfirmationDialogService confirmationDialogService,
        ILocalizationService localizationService,
        ILogger<ChangeDeviceConfirmationDialogService> logger)
    {
        _confirmationDialogService = confirmationDialogService;
        _localizationService = localizationService;
        _logger = logger;
    }

    public async Task<bool> ShowChangeDeviceConfirmationAsync(
        string deviceName,
        string deviceSerial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Opening Change Device confirmation dialog for device {Serial}.", deviceSerial);

        bool confirmed = await _confirmationDialogService
            .ShowConfirmationAsync(
                new ConfirmationDialogOptions
                {
                    Caption = string.Format(
                        _localizationService.GetString("ChangeDeviceConfirmation_Caption"),
                        deviceName,
                        deviceSerial),
                    Message = _localizationService.GetString("ChangeDeviceConfirmation_Message"),
                    WarningMessage = _localizationService.GetString("ChangeDeviceConfirmation_Warning"),
                    Icon = ConfirmationDialogIcon.ChangeDevice
                },
                cancellationToken)
            .ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Change Device confirmation dialog closed. Confirmed: {Confirmed}.", confirmed);
        return confirmed;
    }
}
