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

        string message = string.Join(
            Environment.NewLine + Environment.NewLine,
            _localizationService.GetString("ChangeDeviceConfirmation_Title"),
            string.Concat(deviceName, Environment.NewLine, deviceSerial),
            CreateProfileNotice(options),
            CreateCleanNotice(options),
            _localizationService.GetString(
                options.UseDefaultMode || options.ClearAllPackages || options.ClearGoogleAccounts
                    ? "ChangeDeviceConfirmation_GoogleDataMayBeCleared"
                    : "ChangeDeviceConfirmation_GoogleDataPreserved"));

        bool confirmed = await _confirmationDialogService
            .ShowWarningConfirmationAsync(
                message,
                _localizationService.GetString("ChangeDeviceConfirmation_WindowTitle"),
                cancellationToken)
            .ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Change Device confirmation dialog closed. Confirmed: {Confirmed}.", confirmed);
        return confirmed;
    }

    private string CreateCleanNotice(DeviceChangeOptions options)
    {
        if (options.UseDefaultMode)
        {
            return string.Join(
                "; ",
                _localizationService.GetString("ChangeDeviceConfirmation_ClearAllPackages"),
                _localizationService.GetString("ChangeDeviceConfirmation_ClearGoogleAccounts"));
        }

        var operations = new List<string>();
        if (options.ClearAllPackages)
            operations.Add(_localizationService.GetString("ChangeDeviceConfirmation_ClearAllPackages"));
        else if (options.ClearSelectedPackages)
        {
            operations.Add(string.Format(
                _localizationService.GetString("ChangeDeviceConfirmation_ClearSelectedPackages"),
                options.SelectedPackages?.Count ?? 0));
        }

        if (!options.ClearAllPackages && options.ClearGooglePackages)
            operations.Add(_localizationService.GetString("ChangeDeviceConfirmation_ClearGooglePackages"));

        if (!options.ClearAllPackages && options.ClearGoogleAccounts)
            operations.Add(_localizationService.GetString("ChangeDeviceConfirmation_ClearGoogleAccounts"));

        if (options.UseRmRfForPackageCleanup && operations.Count > 0)
            operations.Add(_localizationService.GetString("ChangeDeviceConfirmation_RmRfPackageCleanup"));

        return operations.Count == 0
            ? _localizationService.GetString("ChangeDeviceConfirmation_NoPackageCleanup")
            : string.Join("; ", operations);
    }

    private string CreateProfileNotice(DeviceChangeOptions options)
    {
        if (options.UseDefaultMode)
            return _localizationService.GetString("ChangeDeviceConfirmation_DefaultProfileNotice");

        var operations = new List<string>
        {
            _localizationService.GetString(
                options.ChangeAndroidId
                    ? "ChangeDeviceConfirmation_ChangeAndroidId"
                    : "ChangeDeviceConfirmation_DeleteAndroidId")
        };
        if (options.ChangeMacAddress)
            operations.Add(_localizationService.GetString("ChangeDeviceConfirmation_ChangeMac"));

        return string.Format(
            _localizationService.GetString("ChangeDeviceConfirmation_AdvancedProfileNotice"),
            string.Join("; ", operations));
    }
}
