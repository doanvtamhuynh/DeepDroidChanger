using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.ViewModels;

public sealed partial class RestoreDeviceViewModel : ObservableObject
{
    private readonly IFilePickerDialogService _filePickerDialogService;
    private readonly ILocalizationService _localizationService;
    private readonly IDeviceRestoreService _deviceRestoreService;
    private IReadOnlyList<string> _archivePaths = [];
    private IReadOnlyList<DeviceRestoreInspection> _inspections = [];
    private string _targetDescription = string.Empty;

    [ObservableProperty]
    private string _archivePath = string.Empty;

    [ObservableProperty]
    private string _restorePassword = string.Empty;

    [ObservableProperty]
    private bool _restoreDeviceProperties = true;

    [ObservableProperty]
    private bool _restoreManagedSystemSettings = true;

    [ObservableProperty]
    private bool _restoreUserAppData = true;

    [ObservableProperty]
    private bool _restoreKeybox;

    [ObservableProperty]
    private bool _restoreSsaid;

    [ObservableProperty]
    private bool _restoreGoogleAppData;

    [ObservableProperty]
    private bool _restoreGoogleAccountState;

    [ObservableProperty]
    private DeviceRestoreInspection? _inspection;

    [ObservableProperty]
    private bool _isInspecting;

    [ObservableProperty]
    private string _inspectionError = string.Empty;

    public RestoreDeviceViewModel(
        IFilePickerDialogService filePickerDialogService,
        ILocalizationService localizationService,
        IDeviceRestoreService deviceRestoreService)
    {
        _filePickerDialogService = filePickerDialogService;
        _localizationService = localizationService;
        _deviceRestoreService = deviceRestoreService;
    }

    public event Action<bool>? CloseRequested;

    public bool IsBatch => _archivePaths.Count > 1;

    public string TargetDescription => _targetDescription;

    public string PackageSummary
    {
        get
        {
            if (Inspection is null)
                return string.Empty;

            DeviceRestoreInspection[] inspections = _inspections.Count > 0
                ? _inspections.ToArray()
                : [Inspection];
            int userPackageCount = inspections
                .SelectMany(item => item.Packages)
                .Count(item => string.Equals(item.Group, "apps", StringComparison.Ordinal));
            int googlePackageCount = inspections
                .SelectMany(item => item.Packages)
                .Count(item => string.Equals(item.Group, "google", StringComparison.Ordinal));
            return string.Format(
                _localizationService.GetString("RestoreDevice_PackageSummaryFormat"),
                userPackageCount,
                googlePackageCount,
                inspections.Length);
        }
    }

    public string InspectionWarnings
    {
        get
        {
            if (Inspection is null)
                return string.Empty;

            var warnings = _inspections
                .SelectMany(item => item.Warnings)
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.Ordinal)
                .Select(FormatWarning)
                .ToList();
            if (_inspections.Any(item =>
                    item.FindOptionalComponent("googleAccountState")?.State == "partial"))
            {
                warnings.Add(_localizationService.GetString("RestoreDevice_PartialAccountWarning"));
            }

            return string.Join(Environment.NewLine, warnings.Distinct(StringComparer.Ordinal));
        }
    }

    public bool HasInspectionWarnings => !string.IsNullOrWhiteSpace(InspectionWarnings);

    public bool CanRestoreDeviceProperties =>
        IsAvailableAcrossArchives(inspection => inspection.HasDeviceProperties);

    public bool CanRestoreManagedSystemSettings =>
        IsAvailableAcrossArchives(inspection => inspection.HasManagedSystemSettings);

    public bool CanRestoreUserAppData =>
        IsAvailableAcrossArchives(inspection => inspection.HasUserAppData);

    public bool CanRestoreKeybox =>
        IsAvailableAcrossArchives(inspection => inspection.HasKeybox);

    public bool CanRestoreSsaid =>
        IsAvailableAcrossArchives(inspection => inspection.HasSsaid);

    public bool CanRestoreGoogleAppData =>
        IsAvailableAcrossArchives(inspection => inspection.HasGoogleAppData);

    public bool CanRestoreGoogleAccountState =>
        IsAvailableAcrossArchives(inspection => inspection.HasGoogleAccountState);

    public bool CanConfirm =>
        !IsInspecting
        && _archivePaths.Count > 0
        && _archivePaths.All(File.Exists)
        && RestorePassword.Length >= DeviceRestoreOptions.MinimumRestorePasswordLength
        && (Inspection is null || HasSelectedAvailableComponent());

    public bool IsRestorePasswordTooShort =>
        !string.IsNullOrEmpty(RestorePassword)
        && RestorePassword.Length < DeviceRestoreOptions.MinimumRestorePasswordLength;

    public string SelectedArchiveName =>
        _archivePaths.Count switch
        {
            0 => string.Empty,
            1 => Path.GetFileName(_archivePaths[0]),
            _ => string.Format(
                _localizationService.GetString("RestoreDevice_ArchiveCountFormat"),
                _archivePaths.Count)
        };

    [RelayCommand]
    private void BrowseArchive()
    {
        string? selected = _filePickerDialogService.ShowOpenFileDialog(
            _localizationService.GetString("RestoreDevice_FileFilter"),
            _localizationService.GetString("RestoreDevice_BrowseTitle"));
        if (!string.IsNullOrWhiteSpace(selected))
            SetArchivePaths([selected]);
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (!CanConfirm)
            return;

        if (Inspection is null)
        {
            IsInspecting = true;
            InspectionError = string.Empty;
            try
            {
                DeviceRestoreInspection[] inspections = await Task.WhenAll(
                        _archivePaths.Select(path => _deviceRestoreService
                            .InspectAsync(path, RestorePassword, CancellationToken.None)))
                    .ConfigureAwait(true);
                _inspections = inspections;
                Inspection = inspections[0];
                RestoreDeviceProperties = CanRestoreDeviceProperties;
                RestoreManagedSystemSettings = CanRestoreManagedSystemSettings;
                RestoreUserAppData = CanRestoreUserAppData;
                RestoreKeybox = false;
                RestoreSsaid = false;
                RestoreGoogleAppData = false;
                RestoreGoogleAccountState = false;
                NotifyStateChanged();
            }
            catch (Exception exception)
            {
                InspectionError = exception.Message;
            }
            finally
            {
                IsInspecting = false;
                NotifyStateChanged();
            }

            return;
        }

        if (HasSelectedAvailableComponent())
            CloseRequested?.Invoke(true);
    }

    public void SetArchivePaths(IReadOnlyList<string> archivePaths)
    {
        ArgumentNullException.ThrowIfNull(archivePaths);
        string[] normalizedPaths = archivePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
            throw new ArgumentException("At least one restore archive is required.", nameof(archivePaths));

        _archivePaths = normalizedPaths;
        _inspections = [];
        ArchivePath = normalizedPaths[0];
        Inspection = null;
        InspectionError = string.Empty;
        NotifyStateChanged();
    }

    public void SetTargetDescription(string targetDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDescription);
        _targetDescription = targetDescription.Trim();
        NotifyStateChanged();
    }

    public DeviceRestoreOptions BuildOptions()
    {
        if (!CanConfirm || Inspection is null)
            throw new InvalidOperationException("The restore configuration is invalid.");

        return new DeviceRestoreOptions(
            ArchivePath.Trim(),
            RestoreDeviceProperties,
            RestoreManagedSystemSettings,
            RestoreUserAppData,
            RestoreKeybox,
            RestoreSsaid,
            RestoreGoogleAppData,
            RestoreGoogleAccountState)
        {
            RestorePassword = RestorePassword
        };
    }

    public DeviceRestoreBatchOptions BuildBatchOptions()
    {
        if (!CanConfirm
            || _inspections.Count != _archivePaths.Count
            || !HasSelectedAvailableComponent())
        {
            throw new InvalidOperationException("The batch restore configuration is invalid.");
        }

        return new DeviceRestoreBatchOptions(
            _archivePaths,
            RestoreDeviceProperties,
            RestoreManagedSystemSettings,
            RestoreUserAppData,
            RestoreKeybox,
            RestoreSsaid,
            RestoreGoogleAppData,
            RestoreGoogleAccountState)
        {
            RestorePassword = RestorePassword,
            Inspections = _inspections
        };
    }

    public void ClearSensitiveInputs()
    {
        RestorePassword = string.Empty;
    }

    partial void OnArchivePathChanged(string value)
    {
        if (_archivePaths.Count <= 1
            || _archivePaths.Count == 0
            || !string.Equals(_archivePaths[0], value, StringComparison.OrdinalIgnoreCase))
        {
            _archivePaths = string.IsNullOrWhiteSpace(value) ? [] : [value];
        }

        _inspections = [];
        Inspection = null;
        InspectionError = string.Empty;
        NotifyStateChanged();
    }

    partial void OnRestorePasswordChanged(string value)
    {
        _inspections = [];
        Inspection = null;
        InspectionError = string.Empty;
        NotifyStateChanged();
    }

    partial void OnRestoreDevicePropertiesChanged(bool value) => NotifyStateChanged();
    partial void OnRestoreManagedSystemSettingsChanged(bool value) => NotifyStateChanged();
    partial void OnRestoreUserAppDataChanged(bool value) => NotifyStateChanged();
    partial void OnRestoreKeyboxChanged(bool value) => NotifyStateChanged();
    partial void OnRestoreSsaidChanged(bool value) => NotifyStateChanged();
    partial void OnRestoreGoogleAppDataChanged(bool value) => NotifyStateChanged();
    partial void OnRestoreGoogleAccountStateChanged(bool value) => NotifyStateChanged();
    partial void OnIsInspectingChanged(bool value) => NotifyStateChanged();

    private string FormatWarning(string warning)
    {
        return warning switch
        {
            "google_account_state_same_device_restore_only" =>
                _localizationService.GetString("RestoreDevice_SameDeviceAccountWarning"),
            "google_account_state_snapshot_unavailable" =>
                _localizationService.GetString("RestoreDevice_AccountSnapshotUnavailableWarning"),
            _ => warning
        };
    }

    private bool HasSelectedAvailableComponent()
    {
        return (RestoreDeviceProperties && CanRestoreDeviceProperties)
            || (RestoreManagedSystemSettings && CanRestoreManagedSystemSettings)
            || (RestoreUserAppData && CanRestoreUserAppData)
            || (RestoreKeybox && CanRestoreKeybox)
            || (RestoreSsaid && CanRestoreSsaid)
            || (RestoreGoogleAppData && CanRestoreGoogleAppData)
            || (RestoreGoogleAccountState && CanRestoreGoogleAccountState);
    }

    private bool IsAvailableAcrossArchives(Func<DeviceRestoreInspection, bool> selector)
    {
        return Inspection is not null
            && (_inspections.Count == 0 || _inspections.All(selector));
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(IsRestorePasswordTooShort));
        OnPropertyChanged(nameof(SelectedArchiveName));
        OnPropertyChanged(nameof(IsBatch));
        OnPropertyChanged(nameof(TargetDescription));
        OnPropertyChanged(nameof(PackageSummary));
        OnPropertyChanged(nameof(InspectionWarnings));
        OnPropertyChanged(nameof(HasInspectionWarnings));
        OnPropertyChanged(nameof(CanRestoreDeviceProperties));
        OnPropertyChanged(nameof(CanRestoreManagedSystemSettings));
        OnPropertyChanged(nameof(CanRestoreUserAppData));
        OnPropertyChanged(nameof(CanRestoreKeybox));
        OnPropertyChanged(nameof(CanRestoreSsaid));
        OnPropertyChanged(nameof(CanRestoreGoogleAppData));
        OnPropertyChanged(nameof(CanRestoreGoogleAccountState));
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
