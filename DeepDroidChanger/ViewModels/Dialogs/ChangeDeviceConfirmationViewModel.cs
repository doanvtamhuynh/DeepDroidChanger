using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.ViewModels;

public sealed partial class ChangeDeviceConfirmationViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;

    public ChangeDeviceConfirmationViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private string _deviceSerial = string.Empty;

    [ObservableProperty]
    private string _profileNotice = string.Empty;

    [ObservableProperty]
    private string _cleanNotice = string.Empty;

    [ObservableProperty]
    private string _googleNotice = string.Empty;

    public event EventHandler<bool>? CloseRequested;

    public void Initialize(string deviceName, string deviceSerial, DeviceChangeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        DeviceName = deviceName;
        DeviceSerial = deviceSerial;
        ProfileNotice = CreateProfileNotice(options);
        CleanNotice = CreateCleanNotice(options);
        GoogleNotice = _localizationService.GetString(
            options.UseDefaultMode || options.ClearAllPackages || options.ClearGoogleAccounts
                ? "ChangeDeviceConfirmation_GoogleDataMayBeCleared"
                : "ChangeDeviceConfirmation_GoogleDataPreserved");
    }

    [RelayCommand]
    private void Confirm()
    {
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, false);
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

        var operations = new List<string>();
        if (options.ChangeAndroidId)
            operations.Add(_localizationService.GetString("ChangeDeviceConfirmation_ChangeAndroidId"));
        else
            operations.Add(_localizationService.GetString("ChangeDeviceConfirmation_DeleteAndroidId"));
        if (options.ChangeMacAddress)
            operations.Add(_localizationService.GetString("ChangeDeviceConfirmation_ChangeMac"));
        return string.Format(
            _localizationService.GetString("ChangeDeviceConfirmation_AdvancedProfileNotice"),
            string.Join("; ", operations));
    }
}
