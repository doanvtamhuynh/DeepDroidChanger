using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels;

/// <summary>Configuration only; the main workspace owns execution and feedback.</summary>
public sealed partial class TwrpWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly ITwrpRecoveryService _service;
    private readonly TwrpArchiveService _archives;
    private readonly IFilePickerDialogService _picker;
    private readonly ILocalizationService _localization;
    private readonly ILogger<TwrpWorkspaceViewModel> _logger;
    private readonly IConfirmationDialogService _confirmation;
    private CancellationToken _configurationToken;
    private TwrpBackupManifest? _manifest;
    private string _serial = "";
    private string _marker = "";
    private string? _bundledRecoveryImagePath;
    public bool UsesInstalledRecovery => _marker == "starlte";
    public bool UsesBundledRecoveryImage => !UsesInstalledRecovery && _bundledRecoveryImagePath is not null;
    public bool RequiresRecoveryImageSelection => !UsesInstalledRecovery && !UsesBundledRecoveryImage;
    public bool CanBrowseRecoveryImage => CanEdit && RequiresRecoveryImageSelection;
    public event Action<TwrpDialogResult>? Submitted;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRestoreMode;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _backupName = "";
    [ObservableProperty] private string _backupDirectory;
    [ObservableProperty] private string _archiveSource = "";
    [ObservableProperty] private string _backupSummary = "";
    [ObservableProperty] private string _recoveryImagePath = "";
    public bool CanEdit => !IsBusy;
    public string DeviceSerial => _serial;
    public string DeviceHeading => DeviceInfoTextHelper.Create(_localization, DeviceName, _serial);
    public string OutputPreview => string.IsNullOrWhiteSpace(BackupName) ? "" : Path.Combine(BackupDirectory, BackupName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? BackupName : BackupName + ".zip");
    private bool HasKnownDeviceType => TwrpRecoveryImage.Available.Any(image => image.Device == _marker);
    public bool CanSubmit => !IsBusy
        && (IsRestoreMode ? _manifest is { Version: 3 or 4 or 5 or 6 } && HasKnownDeviceType
            && _manifest.Device.Codename == _marker && !string.IsNullOrWhiteSpace(ArchiveSource)
            : !string.IsNullOrWhiteSpace(BackupName));
    public TwrpWorkspaceViewModel(ITwrpRecoveryService service, TwrpArchiveService archives, IFilePickerDialogService picker,
        ILocalizationService localization, ILogger<TwrpWorkspaceViewModel> logger, IConfirmationDialogService confirmation)
    {
        _service = service;
        _archives = archives;
        _picker = picker;
        _localization = localization;
        _logger = logger;
        _confirmation = confirmation;
        _backupDirectory = archives.DefaultDirectory;
        _localization.LanguageChanged += LanguageChanged;
    }
    public async Task InitializeAsync(string serial, bool restore, CancellationToken token, string? deviceName = null)
    {
        DeviceName = deviceName ?? "";
        _configurationToken = token;
        _serial = serial;
        IsRestoreMode = restore;
        OnPropertyChanged(nameof(DeviceSerial));
        OnPropertyChanged(nameof(DeviceHeading));
        IsBusy = true;
        try
        {
            // Resolve the marker and bundled image before exposing manual selection.
            string marker = "";
            try
            {
                marker = await _service.GetDeviceMarkerAsync(serial, token) ?? "";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not prefill the TWRP image for {Serial}.", serial);
            }
            _marker = marker;
            _bundledRecoveryImagePath = null;
            if (!UsesInstalledRecovery)
            {
                try
                {
                    string? image = _service.GetDefaultImagePath(marker);
                    if (!string.IsNullOrWhiteSpace(image)) _bundledRecoveryImagePath = image;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not locate the bundled TWRP image for {Serial}.", serial);
                }
            }
            OnPropertyChanged(nameof(UsesInstalledRecovery));
            OnPropertyChanged(nameof(UsesBundledRecoveryImage));
            OnPropertyChanged(nameof(RequiresRecoveryImageSelection));
            RecoveryImagePath = _bundledRecoveryImagePath ?? "";
            string nameMarker = string.Concat((string.IsNullOrWhiteSpace(marker) ? "recovery" : marker)
                .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            BackupName = _archives.DefaultName(nameMarker, serial);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { IsBusy = false; }
    }
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        UpdateCommands();
    }
    partial void OnRecoveryImagePathChanged(string value) => UpdateCommands();
    partial void OnDeviceNameChanged(string value) => OnPropertyChanged(nameof(DeviceHeading));
    partial void OnBackupNameChanged(string value)
    {
        OnPropertyChanged(nameof(OutputPreview));
        UpdateCommands();
    }
    partial void OnBackupDirectoryChanged(string value) => OnPropertyChanged(nameof(OutputPreview));
    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanBrowseRecoveryImage));
        SubmitCommand.NotifyCanExecuteChanged();
        BrowseBackupCommand.NotifyCanExecuteChanged();
        BrowseArchiveCommand.NotifyCanExecuteChanged();
        BrowseRecoveryImageCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanBrowseRecoveryImage))]
    private void BrowseRecoveryImage()
    {
        try
        {
            string initialDirectory = Path.GetDirectoryName(RecoveryImagePath) ?? Path.Combine(AppContext.BaseDirectory, "Assets", "TWRP");
            string? path = _picker.ShowOpenFileDialog("TWRP image (*.img)|*.img", _localization.GetString("Twrp_ChooseImage"), initialDirectory);
            if (path is not null) RecoveryImagePath = path;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void BrowseBackup()
    {
        string? path = _picker.ShowOpenFolderDialog(_localization.GetString("Twrp_PcDirectory"), BackupDirectory);
        if (path is not null) BackupDirectory = path;
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task BrowseArchiveAsync()
    {
        IsBusy = true;
        Error = "";
        try
        {
            string initialDirectory = _archives.EnsureDefaultDirectory();
            string? path = _picker.ShowOpenFileDialog("TWRP backup (*.zip)|*.zip", _localization.GetString("Twrp_ChooseArchive"), initialDirectory);
            if (path is null) return;
            await LoadRestoreArchiveAsync(path, CancellationToken.None);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _configurationToken);
        IsBusy = true;
        try
        {
            Error = "";
            string? imagePath = GetSelectedImagePath();
            if (IsRestoreMode)
            {
                if (_manifest is not { Version: 3 or 4 or 5 or 6 }) throw new TwrpWorkflowException("Twrp_RestoreFormatRequired");
                // Re-read the ZIP in case it changed after selection.
                if (!await LoadRestoreArchiveAsync(ArchiveSource, lifetime.Token)) return;
            }
            else
            {
                TwrpSafety.CliBackupName(_archives.OutputPath(BackupDirectory, BackupName));
            }
            var request = CreateRequest(imagePath);
            string actionKey = IsRestoreMode ? "Twrp_RestoreAction" : "Twrp_BackupAction";
            bool confirmed = await _confirmation.ShowConfirmationAsync(new()
            {
                Caption = _localization.GetString(IsRestoreMode ? "Twrp_RestoreTitle" : "Twrp_BackupTitle"),
                Message = DeviceHeading + Environment.NewLine + _localization.GetString("Twrp_StartConfirm")
                    + Environment.NewLine + (UsesInstalledRecovery ? _localization.GetString("Twrp_InstalledRecovery") : "TWRP: " + imagePath),
                WarningMessage = _localization.GetString("Twrp_StartWarning"),
                ConfirmButtonText = _localization.GetString(actionKey), CancelButtonText = _localization.GetString("Twrp_Cancel"),
                Icon = ConfirmationDialogIcon.Warning
            }, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (confirmed) Submitted?.Invoke(new(request));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex); }
        finally { IsBusy = false; }
    }
    private string? GetSelectedImagePath()
    {
        string? selectedImage = UsesBundledRecoveryImage ? _bundledRecoveryImagePath : RecoveryImagePath;
        if (UsesInstalledRecovery || string.IsNullOrWhiteSpace(selectedImage)) return null;

        string path = Path.GetFullPath(selectedImage);
        if (!string.Equals(Path.GetExtension(path), ".img", StringComparison.OrdinalIgnoreCase))
            throw new TwrpWorkflowException("Twrp_InvalidImage");
        return path;
    }

    private TwrpRequest CreateRequest(string? imagePath) => new(
        _serial, TwrpSafety.Confirmation,
        BackupName: IsRestoreMode ? null : BackupName,
        BackupDirectory: IsRestoreMode ? null : BackupDirectory,
        SourceArchivePath: IsRestoreMode ? ArchiveSource : null,
        RecoveryDevice: string.IsNullOrEmpty(_marker) ? null : _marker,
        SourceImagePath: imagePath);

    private async Task<bool> LoadRestoreArchiveAsync(string path, CancellationToken token)
    {
        SetRestoreArchive(null, "");
        var manifest = await _archives.ReadAsync(path, token);
        if (manifest.Version is not 3 and not 4 and not 5 and not 6)
            throw new TwrpWorkflowException("Twrp_RestoreFormatRequired");
        TwrpSafety.CliBackupName(path);
        if (!CheckRestoreDeviceType(manifest)) return false;

        SetRestoreArchive(manifest, path);
        return true;
    }

    private void SetRestoreArchive(TwrpBackupManifest? manifest, string path)
    {
        _manifest = manifest;
        ArchiveSource = path;
        UpdateBackupSummary();
    }

    private bool CheckRestoreDeviceType(TwrpBackupManifest manifest)
    {
        if (!HasKnownDeviceType)
        {
            Error = _localization.GetString("Twrp_RestoreDeviceTypeUnknown");
            return false;
        }
        if (manifest.Device.Codename != _marker)
        {
            Error = string.Format(_localization.GetString("Twrp_BackupDeviceMismatch"), _marker, manifest.Device.Codename);
            return false;
        }
        return true;
    }
    private void ShowError(Exception ex)
    {
        _logger.LogWarning(ex, "Invalid TWRP configuration for {Serial}.", _serial);
        Error = _localization.GetString(ex is TwrpWorkflowException known ? known.MessageKey : "Twrp_WorkflowFailed");
    }
    private void UpdateBackupSummary()
    {
        BackupSummary = _manifest is null ? "" : string.Format(_localization.GetString("Twrp_ArchiveInfoFormat"),
            _manifest.Name, _manifest.CreatedAt.ToLocalTime().ToString("g"), _manifest.Device.Codename, _manifest.Device.Serial,
            string.Join(", ", _manifest.Partitions.Select(p => _localization.GetString("Twrp_Part_" + p))));
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(DeviceHeading));
        UpdateBackupSummary();
    }
    public void Dispose()
    {
        _localization.LanguageChanged -= LanguageChanged;
    }
}
