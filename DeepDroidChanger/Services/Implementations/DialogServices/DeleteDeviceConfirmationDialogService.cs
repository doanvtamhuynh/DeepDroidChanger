using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeleteDeviceConfirmationDialogService : IDeleteDeviceConfirmationDialogService
{
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<DeleteDeviceConfirmationDialogService> _logger;

    public DeleteDeviceConfirmationDialogService(
        IConfirmationDialogService confirmationDialogService,
        ILocalizationService localizationService,
        ILogger<DeleteDeviceConfirmationDialogService> logger)
    {
        _confirmationDialogService = confirmationDialogService;
        _localizationService = localizationService;
        _logger = logger;
    }

    public async Task<bool> ShowDeleteDeviceConfirmationAsync(
        string deviceName,
        string deviceSerial,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Opening Delete Device confirmation dialog for device {Serial}.", deviceSerial);

        bool confirmed = await _confirmationDialogService
            .ShowConfirmationAsync(
                new ConfirmationDialogOptions
                {
                    Caption = string.Format(
                        _localizationService.GetString("DeleteDeviceConfirmation_Caption"),
                        deviceName,
                        deviceSerial),
                    Message = _localizationService.GetString("DeleteDeviceConfirmation_Message"),
                    WarningMessage = _localizationService.GetString("DeleteDeviceConfirmation_Warning"),
                    Icon = ConfirmationDialogIcon.Delete
                },
                cancellationToken)
            .ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Delete Device confirmation dialog closed. Confirmed: {Confirmed}.", confirmed);
        return confirmed;
    }
}
