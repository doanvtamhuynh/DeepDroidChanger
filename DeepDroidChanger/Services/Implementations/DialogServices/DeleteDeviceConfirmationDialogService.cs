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

        string message = string.Join(
            Environment.NewLine + Environment.NewLine,
            _localizationService.GetString("DeleteDeviceConfirmation_Title"),
            string.Concat(deviceName, Environment.NewLine, deviceSerial),
            _localizationService.GetString("DeleteDeviceConfirmation_Message"));
        bool confirmed = await _confirmationDialogService
            .ShowWarningConfirmationAsync(
                message,
                _localizationService.GetString("DeleteDeviceConfirmation_WindowTitle"),
                cancellationToken)
            .ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Delete Device confirmation dialog closed. Confirmed: {Confirmed}.", confirmed);
        return confirmed;
    }
}
