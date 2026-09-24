using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.ViewModels;

public sealed partial class BackupDeviceViewModel : ObservableObject
{
    private readonly IFilePickerDialogService _filePickerDialogService;
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    private bool _includeDeviceProperties = true;

    [ObservableProperty]
    private bool _includeManagedSystemSettings = true;

    [ObservableProperty]
    private bool _includeUserAppData = true;

    [ObservableProperty]
    private bool _includeKeybox;

    [ObservableProperty]
    private bool _includeSsaid;

    [ObservableProperty]
    private bool _includeGoogleAppData;

    [ObservableProperty]
    private bool _includeGoogleAccountState;

    [ObservableProperty]
    private string _destinationDirectory = string.Empty;

    public BackupDeviceViewModel(
        IFilePickerDialogService filePickerDialogService,
        ILocalizationService localizationService)
    {
        _filePickerDialogService = filePickerDialogService;
        _localizationService = localizationService;
    }

    public event Action<bool>? CloseRequested;

    public bool CanConfirm =>
        (IncludeDeviceProperties
         || IncludeManagedSystemSettings
         || IncludeUserAppData
         || IncludeKeybox
         || IncludeSsaid
         || IncludeGoogleAppData
         || IncludeGoogleAccountState)
        && IsDestinationValid();

    public bool IsDestinationInvalid =>
        !string.IsNullOrWhiteSpace(DestinationDirectory)
        && !IsDestinationValid();

    public string DefaultLocationHint => string.Format(
        _localizationService.GetString("BackupDevice_DefaultLocationHint"),
        Path.Combine(AppContext.BaseDirectory, "Backup"));

    public DeviceBackupOptions BuildOptions()
    {
        if (!CanConfirm)
            throw new InvalidOperationException("The backup configuration is invalid.");

        return new DeviceBackupOptions(
            IncludeDeviceProperties,
            IncludeManagedSystemSettings,
            IncludeUserAppData,
            IncludeKeybox,
            IncludeSsaid,
            IncludeGoogleAppData,
            IncludeGoogleAccountState,
            string.IsNullOrWhiteSpace(DestinationDirectory)
                ? null
                : DestinationDirectory.Trim());
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        string? initialDirectory = IsDestinationValid()
            && !string.IsNullOrWhiteSpace(DestinationDirectory)
                ? Path.GetFullPath(DestinationDirectory.Trim())
                : Path.Combine(AppContext.BaseDirectory, "Backup");
        string? selectedDirectory = _filePickerDialogService.ShowOpenFolderDialog(
            _localizationService.GetString("BackupDevice_BrowseFolderTitle"),
            initialDirectory);
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
            DestinationDirectory = selectedDirectory;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (CanConfirm)
            CloseRequested?.Invoke(true);
    }

    partial void OnIncludeDevicePropertiesChanged(bool value) => NotifyValidationChanged();
    partial void OnIncludeManagedSystemSettingsChanged(bool value) => NotifyValidationChanged();
    partial void OnIncludeUserAppDataChanged(bool value) => NotifyValidationChanged();
    partial void OnIncludeKeyboxChanged(bool value) => NotifyValidationChanged();
    partial void OnIncludeSsaidChanged(bool value) => NotifyValidationChanged();
    partial void OnIncludeGoogleAppDataChanged(bool value) => NotifyValidationChanged();
    partial void OnIncludeGoogleAccountStateChanged(bool value) => NotifyValidationChanged();
    partial void OnDestinationDirectoryChanged(string value)
    {
        NotifyValidationChanged();
    }

    private void NotifyValidationChanged()
    {
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(IsDestinationInvalid));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private bool IsDestinationValid()
    {
        if (string.IsNullOrWhiteSpace(DestinationDirectory))
            return true;

        try
        {
            string fullPath = Path.GetFullPath(DestinationDirectory.Trim());
            return !File.Exists(fullPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }
}
