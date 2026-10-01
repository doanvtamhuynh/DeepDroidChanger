using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Constants;
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
    private IReadOnlyList<DeviceRestoreInspectionFailure> _inspectionFailures = [];
    private IReadOnlyList<string> _targetSerials = [];
    private IReadOnlyList<string> _compatibilityWarnings = [];
    private bool _compatibilityChecked;
    private bool _compatibilityBlocked;
    private string _targetDescription = string.Empty;

    [ObservableProperty]
    private string _archivePath = string.Empty;

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

            warnings.AddRange(_compatibilityWarnings);

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
        && !_compatibilityBlocked
        && _archivePaths.Count > 0
        && _archivePaths.All(File.Exists)
        && (Inspection is null || HasSelectedAvailableComponent());

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
            _localizationService.GetString("RestoreDevice_BrowseTitle"),
            BackupPathConstants.EnsureDefaultDirectory());
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
                ArchiveInspectionAttempt[] attempts = await Task.WhenAll(
                        _archivePaths.Select(InspectArchiveSafelyAsync))
                    .ConfigureAwait(true);
                _inspections = attempts
                    .Where(attempt => attempt.Inspection is not null)
                    .Select(attempt => attempt.Inspection!)
                    .ToArray();
                _inspectionFailures = attempts
                    .Where(attempt => attempt.Failure is not null)
                    .Select(attempt => attempt.Failure!)
                    .ToArray();
                Inspection = _inspections.FirstOrDefault();
                InspectionError = FormatInspectionFailures();
                if (Inspection is null)
                    return;

                RestoreDeviceProperties = CanRestoreDeviceProperties;
                RestoreManagedSystemSettings = CanRestoreManagedSystemSettings;
                RestoreUserAppData = CanRestoreUserAppData;
                RestoreKeybox = false;
                RestoreSsaid = false;
                RestoreGoogleAppData = false;
                RestoreGoogleAccountState = false;
                _compatibilityWarnings = [];
                _compatibilityChecked = false;
                _compatibilityBlocked = false;
                NotifyStateChanged();
            }
            catch (OperationCanceledException)
            {
                throw;
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

        if (!_compatibilityChecked)
        {
            await RunCompatibilityPreflightAsync().ConfigureAwait(true);
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
        _inspectionFailures = [];
        _compatibilityWarnings = [];
        _compatibilityChecked = false;
        _compatibilityBlocked = false;
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

    public void SetTargetSerials(IReadOnlyList<string> targetSerials)
    {
        ArgumentNullException.ThrowIfNull(targetSerials);
        _targetSerials = targetSerials
            .Where(serial => !string.IsNullOrWhiteSpace(serial))
            .Select(serial => serial.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
            RestoreGoogleAccountState);
    }

    public DeviceRestoreBatchOptions BuildBatchOptions()
    {
        if (!CanConfirm
            || _inspections.Count == 0
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
            Inspections = _inspections,
            FailedInspections = _inspectionFailures
        };
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
        _inspectionFailures = [];
        _compatibilityWarnings = [];
        _compatibilityChecked = false;
        _compatibilityBlocked = false;
        Inspection = null;
        InspectionError = string.Empty;
        NotifyStateChanged();
    }

    partial void OnRestoreDevicePropertiesChanged(bool value) => ResetCompatibilityAndNotify();
    partial void OnRestoreManagedSystemSettingsChanged(bool value) => ResetCompatibilityAndNotify();
    partial void OnRestoreUserAppDataChanged(bool value) => ResetCompatibilityAndNotify();
    partial void OnRestoreKeyboxChanged(bool value) => ResetCompatibilityAndNotify();
    partial void OnRestoreSsaidChanged(bool value) => ResetCompatibilityAndNotify();
    partial void OnRestoreGoogleAppDataChanged(bool value) => ResetCompatibilityAndNotify();
    partial void OnRestoreGoogleAccountStateChanged(bool value) => ResetCompatibilityAndNotify();
    partial void OnIsInspectingChanged(bool value) => NotifyStateChanged();

    private async Task RunCompatibilityPreflightAsync()
    {
        IsInspecting = true;
        _compatibilityWarnings = [];
        _compatibilityBlocked = false;
        try
        {
            var warnings = new List<string>();
            var preflightTasks = new List<Task<DeviceRestoreCompatibility>>();
            foreach (DeviceRestoreInspection inspection in _inspections)
            {
                string? targetSerial = _targetSerials.FirstOrDefault(serial =>
                    string.Equals(serial, inspection.SourceSerial, StringComparison.OrdinalIgnoreCase));
                if (targetSerial is null && _targetSerials.Count == 1)
                    targetSerial = _targetSerials[0];
                if (targetSerial is null)
                    continue;

                DeviceRestoreOptions options = CreateOptions(inspection);
                if (!options.HasSelectedComponent)
                    continue;
                preflightTasks.Add(_deviceRestoreService
                    .PreflightAsync(targetSerial, options, CancellationToken.None));
            }

            DeviceRestoreCompatibility[] results = await Task.WhenAll(preflightTasks)
                .ConfigureAwait(true);
            foreach (DeviceRestoreCompatibility result in results)
            {
                warnings.AddRange(result.Warnings.Select(FormatWarning));
                foreach (DeviceRestorePackageCompatibility package in result.Packages
                             .Where(package => package.Outcome is DeviceRestoreOutcome.Skipped or DeviceRestoreOutcome.Failed
                                 || !string.IsNullOrWhiteSpace(package.Warning)))
                {
                    warnings.Add(string.Format(
                        _localizationService.GetString("Log_RestoreDevicePackageIssueFormat"),
                        package.PackageName,
                        _localizationService.GetString(
                            GetRestorePackageReasonResourceKey(package.Warning ?? package.Reason))));
                }

                if (!result.IsCompatible)
                {
                    _compatibilityBlocked = true;
                    warnings.Add(_localizationService.GetString("RestoreDevice_AccountIncompatibleWarning"));
                }
            }

            _compatibilityWarnings = warnings
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            _compatibilityChecked = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _compatibilityBlocked = true;
            InspectionError = exception.Message;
        }
        finally
        {
            IsInspecting = false;
            NotifyStateChanged();
        }
    }

    private DeviceRestoreOptions CreateOptions(DeviceRestoreInspection inspection)
    {
        return new DeviceRestoreOptions(
            inspection.ArchivePath,
            RestoreDeviceProperties && inspection.HasDeviceProperties,
            RestoreManagedSystemSettings && inspection.HasManagedSystemSettings,
            RestoreUserAppData && inspection.HasUserAppData,
            RestoreKeybox && inspection.HasKeybox,
            RestoreSsaid && inspection.HasSsaid,
            RestoreGoogleAppData && inspection.HasGoogleAppData,
            RestoreGoogleAccountState && inspection.HasGoogleAccountState);
    }

    private string FormatWarning(string warning)
    {
        return warning switch
        {
            "google_account_state_same_device_restore_only" =>
                _localizationService.GetString("RestoreDevice_SameDeviceAccountWarning"),
            "google_account_state_snapshot_unavailable" =>
                _localizationService.GetString("RestoreDevice_AccountSnapshotUnavailableWarning"),
            "source_serial_differs_from_target" =>
                _localizationService.GetString("RestoreDevice_SourceSerialDiffersWarning"),
            "source_device_role_differs_from_target" =>
                _localizationService.GetString("RestoreDevice_SourceRoleDiffersWarning"),
            "keybox_sensitive_restore" =>
                _localizationService.GetString("RestoreDevice_KeyboxSensitiveWarning"),
            "ssaId_source_serial_differs" =>
                _localizationService.GetString("RestoreDevice_SsaidCrossDeviceWarning"),
            "google_app_data_source_differs" =>
                _localizationService.GetString("RestoreDevice_GoogleAppCrossDeviceWarning"),
            "google_account_state_incompatible" =>
                _localizationService.GetString("RestoreDevice_AccountIncompatibleWarning"),
            "google_account_state_partial" =>
                _localizationService.GetString("RestoreDevice_PartialAccountWarning"),
            _ => warning
        };
    }

    private static string GetRestorePackageReasonResourceKey(string? reason)
    {
        return reason switch
        {
            "package_not_installed" => "Log_RestoreDevicePackageNotInstalled",
            "package_metadata_unavailable" => "Log_RestoreDevicePackageMetadataUnavailable",
            "signature_unavailable" => "Log_RestoreDevicePackageSignatureUnavailable",
            "signature_mismatch" => "Log_RestoreDevicePackageSignatureMismatch",
            "version_unavailable" => "Log_RestoreDevicePackageVersionUnavailable",
            "target_version_older" => "Log_RestoreDevicePackageTargetVersionOlder",
            "target_uid_unavailable" => "Log_RestoreDevicePackageTargetUidUnavailable",
            "payload_missing" => "Log_RestoreDevicePackagePayloadMissing",
            "source_package_unavailable" => "Log_RestoreDevicePackageSourceUnavailable",
            "newer_target_version" => "Log_RestoreDevicePackageNewerTargetVersion",
            "target_sdk_differs" => "Log_RestoreDevicePackageTargetSdkDiffers",
            _ => "Log_RestoreDevicePackageRestoreFailed"
        };
    }

    private async Task<ArchiveInspectionAttempt> InspectArchiveSafelyAsync(string archivePath)
    {
        try
        {
            DeviceRestoreInspection inspection = await _deviceRestoreService
                .InspectAsync(archivePath, CancellationToken.None)
                .ConfigureAwait(true);
            return new(inspection, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(
                null,
                new DeviceRestoreInspectionFailure(archivePath, exception.Message));
        }
    }

    private string FormatInspectionFailures()
    {
        return string.Join(
            Environment.NewLine,
            _inspectionFailures.Select(failure => string.Format(
                _localizationService.GetString("RestoreDevice_InspectionFailureFormat"),
                Path.GetFileName(failure.ArchivePath),
                failure.Reason)));
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
        return GetAvailableInspections().Any(selector);
    }

    private IEnumerable<DeviceRestoreInspection> GetAvailableInspections()
    {
        if (_inspections.Count == 0)
            return Inspection is null ? [] : [Inspection];

        if (!IsBatch)
            return _inspections;

        HashSet<string> mappedSerials = _targetSerials
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _inspections.Where(inspection =>
            mappedSerials.Contains(inspection.SourceSerial));
    }

    private void ResetCompatibilityAndNotify()
    {
        _compatibilityWarnings = [];
        _compatibilityChecked = false;
        _compatibilityBlocked = false;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CanConfirm));
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

    private sealed record ArchiveInspectionAttempt(
        DeviceRestoreInspection? Inspection,
        DeviceRestoreInspectionFailure? Failure);
}
