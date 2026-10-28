using System.Collections.ObjectModel;
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
    private bool _settingAll;
    public event Action<TwrpDialogResult>? Submitted;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRestoreMode;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _backupName = "";
    [ObservableProperty] private string _backupDirectory;
    [ObservableProperty] private string _archiveSource = "";
    [ObservableProperty] private string _backupSummary = "";
    [ObservableProperty] private string _storageRoot = "/data/media/0";
    [ObservableProperty] private string? _stagingRoot;
    [ObservableProperty] private bool _skipSpaceCheck;
    [ObservableProperty] private string _recoveryImagePath = "";
    [ObservableProperty] private bool _verifyDigest;
    [ObservableProperty] private bool _canVerifyDigest;
    [ObservableProperty] private TwrpSessionRow? _selectedRecovery;
    [ObservableProperty] private TwrpSessionRow? _selectedExport;
    public ObservableCollection<TwrpPartitionOption> Partitions { get; } = [];
    public ObservableCollection<string> StagingRoots { get; } = [];
    public ObservableCollection<TwrpSessionRow> RecoverySessions { get; } = [];
    public ObservableCollection<TwrpSessionRow> ExportSessions { get; } = [];
    public bool CanEdit => !IsBusy;
    public string DeviceSerial => _serial;
    public string DeviceHeading => DeviceInfoTextHelper.Create(_localization, DeviceName, _serial);
    public string RescueDirectory => _service.DefaultRescueDirectory;
    public string OutputPreview => string.IsNullOrWhiteSpace(BackupName) ? "" : Path.Combine(BackupDirectory, BackupName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? BackupName : BackupName + ".zip");
    public bool HasRecoverySessions => RecoverySessions.Count > 0;
    public bool HasExportSessions => ExportSessions.Count > 0;
    public bool CanSubmit => !IsBusy && RecoverySessions.Count == 0 && !ExportSessions.Any(row => !row.Session.IsTerminal)
        && (IsRestoreMode ? _manifest is { Version: 3 or 4 or 5 or 6 } : !string.IsNullOrWhiteSpace(BackupName));
    public bool CanRecover => !IsBusy && SelectedRecovery is not null;
    public bool CanRetry => !IsBusy && SelectedExport is not null;
    public bool AllSelected
    {
        get
        {
            var available = Partitions.Where(p => p.IsAvailable).ToArray();
            int count = available.Count(p => p.IsSelected);
            return count > 0 && count == available.Length;
        }
        set
        {
            _settingAll = true;
            foreach (var p in Partitions.Where(p => p.IsAvailable)) p.IsSelected = value;
            _settingAll = false;
            RefreshSelection();
        }
    }

    public TwrpWorkspaceViewModel(ITwrpRecoveryService service, TwrpArchiveService archives, IFilePickerDialogService picker,
        ILocalizationService localization, ILogger<TwrpWorkspaceViewModel> logger, IConfirmationDialogService confirmation)
    {
        _service = service; _archives = archives; _picker = picker; _localization = localization; _logger = logger;
        _confirmation = confirmation;
        _backupDirectory = archives.DefaultDirectory;
        _localization.LanguageChanged += LanguageChanged;
    }
    public async Task InitializeAsync(string serial, bool restore, CancellationToken token, string? deviceName = null)
    {
        DeviceName = deviceName ?? "";
        _configurationToken = token;
        _serial = serial; IsRestoreMode = restore; OnPropertyChanged(nameof(DeviceSerial)); OnPropertyChanged(nameof(DeviceHeading)); IsBusy = true;
        SetPartitions(TwrpPartitionCatalog.All);
        StagingRoot = "/data/local/tmp";
        try
        {
            // Resolve the marker and bundled image before exposing manual selection.
            string marker = "";
            try { marker = await _service.GetDeviceMarkerAsync(serial, token) ?? ""; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _logger.LogDebug(ex, "Could not prefill the TWRP image for {Serial}.", serial); }
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
                { _logger.LogDebug(ex, "Could not locate the bundled TWRP image for {Serial}.", serial); }
            }
            OnPropertyChanged(nameof(UsesInstalledRecovery));
            OnPropertyChanged(nameof(UsesBundledRecoveryImage));
            OnPropertyChanged(nameof(RequiresRecoveryImageSelection));
            RecoveryImagePath = _bundledRecoveryImagePath ?? "";
            if (!string.IsNullOrWhiteSpace(marker))
            {
                string nameMarker = string.Concat(marker.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                BackupName = _archives.DefaultName(nameMarker, serial);
            }
            var history = await _service.ListAsync(serial, token);
            foreach (var s in history.Where(s => (!s.IsTerminal && !(s.ExecutionMode == TwrpExecutionMode.ExternalRecovery && s.CliBackup && s.NativeCompleted && !s.CleanupCompleted)) || s.CliRestore && s.State == TwrpSessionState.Completed && !s.CleanupCompleted)) RecoverySessions.Add(new(s, _localization));
            foreach (var s in history.Where(s => s.ExecutionMode == TwrpExecutionMode.ExternalRecovery ? s.CliBackup && s.NativeCompleted && !s.CleanupCompleted : s.Kind == TwrpOperationKind.Backup && (s.State == TwrpSessionState.Completed || s.State == TwrpSessionState.Failed && s.CliBackup && s.NativeBackupCompleted && s.BootRestored && !s.StopRequested) && s.ArchivePath is not null && s.ExportState != TwrpExportState.Completed && !s.Partitions.Contains("media"))) ExportSessions.Add(new(s, _localization));
            SelectedRecovery = RecoverySessions.FirstOrDefault(); SelectedExport = ExportSessions.FirstOrDefault();
            OnPropertyChanged(nameof(HasRecoverySessions)); OnPropertyChanged(nameof(HasExportSessions));
        }
        catch (Exception ex) { ShowError(ex); }
        finally { IsBusy = false; }
    }
    private void SetPartitions(IEnumerable<TwrpPartition> partitions)
    {
        foreach (var p in Partitions) p.PropertyChanged -= PartitionChanged;
        Partitions.Clear();
        var supplied = partitions.ToDictionary(p => p.Id);
        foreach (var known in TwrpPartitionCatalog.All.Where(p => p.Id == "data"))
        {
            var p = supplied.GetValueOrDefault(known.Id) ?? known;
            var item = new TwrpPartitionOption(p, _localization, IsRestoreMode && p.Id == "boot" ? "Twrp_BootRescueOnly" : null)
            { IsSelected = IsRestoreMode ? _manifest?.Partitions.Contains(p.Id) == true && !p.Sensitive && p.Id != "boot" : p.Id is "data" or "super" };
            item.PropertyChanged += PartitionChanged;
            Partitions.Add(item);
        }
        RefreshSelection();
    }
    private void PartitionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_settingAll && e.PropertyName is nameof(TwrpPartitionOption.IsSelected) or nameof(TwrpPartitionOption.IsAvailable)) RefreshSelection();
    }
    private void RefreshSelection() { UpdateDigest(); UpdateCommands(); OnPropertyChanged(nameof(AllSelected)); }
    private string[] Selection() => Partitions.Where(p => p.IsSelected && p.IsAvailable).Select(p => p.Partition.Id).ToArray();
    private string[] RestoreSelection() => Selection().Where(id => id != "boot" && _manifest?.Partitions.Contains(id) == true).ToArray();
    private void UpdateDigest()
    {
        string[] selection = RestoreSelection();
        CanVerifyDigest = _manifest is not null && selection.Length > 0 && _archives.HasDigests(_manifest, selection); VerifyDigest = CanVerifyDigest;
    }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanEdit)); UpdateCommands(); }
    partial void OnStagingRootChanged(string? value) => UpdateCommands();
    partial void OnRecoveryImagePathChanged(string value) => UpdateCommands();
    partial void OnDeviceNameChanged(string value) => OnPropertyChanged(nameof(DeviceHeading));
    partial void OnSelectedRecoveryChanged(TwrpSessionRow? value) => UpdateCommands();
    partial void OnSelectedExportChanged(TwrpSessionRow? value) => UpdateCommands();
    partial void OnBackupNameChanged(string value) { OnPropertyChanged(nameof(OutputPreview)); UpdateCommands(); }
    partial void OnBackupDirectoryChanged(string value) => OnPropertyChanged(nameof(OutputPreview));
    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(CanSubmit)); OnPropertyChanged(nameof(CanRecover)); OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanBrowseRecoveryImage));
        SubmitCommand.NotifyCanExecuteChanged(); RecoverCommand.NotifyCanExecuteChanged(); RetryExportCommand.NotifyCanExecuteChanged();
        BrowseBackupCommand.NotifyCanExecuteChanged(); BrowseArchiveCommand.NotifyCanExecuteChanged(); BrowseRecoveryImageCommand.NotifyCanExecuteChanged();
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
        string? path = _picker.ShowOpenFolderDialog(_localization.GetString("Twrp_PcDirectory"), BackupDirectory); if (path is not null) BackupDirectory = path;
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task BrowseArchiveAsync()
    {
        IsBusy = true; Error = "";
        try
        {
            string initialDirectory = _archives.EnsureDefaultDirectory();
            string? path = _picker.ShowOpenFileDialog("TWRP backup (*.zip)|*.zip", _localization.GetString("Twrp_ChooseArchive"), initialDirectory);
            if (path is null) return;
            _manifest = null; SetPartitions(TwrpPartitionCatalog.All); BackupSummary = ""; ArchiveSource = "";
            var manifest = await _archives.ReadAsync(path, CancellationToken.None);
            if (manifest.Version is not 3 and not 4 and not 5 and not 6) throw new TwrpWorkflowException("Twrp_RestoreFormatRequired");
            TwrpSafety.CliBackupName(path);
            string marker = await _service.GetDeviceMarkerAsync(_serial, CancellationToken.None);
            if (manifest.Device.Codename != marker || manifest.Device.Serial != _serial) throw new TwrpWorkflowException("Twrp_BackupTypeMismatch");
            _manifest = manifest; ArchiveSource = path; UpdateBackupSummary();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { UpdateDigest(); IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _configurationToken);
        IsBusy = true;
        try
        {
            Error = ""; TwrpSafety.Storage(StorageRoot);
            string? selectedImage = UsesBundledRecoveryImage ? _bundledRecoveryImagePath : RecoveryImagePath;
            string? imagePath = UsesInstalledRecovery || string.IsNullOrWhiteSpace(selectedImage) ? null : Path.GetFullPath(selectedImage);
            if (imagePath is not null && !string.Equals(Path.GetExtension(imagePath), ".img", StringComparison.OrdinalIgnoreCase)) throw new TwrpWorkflowException("Twrp_InvalidImage");
            if (IsRestoreMode)
            {
                if (_manifest is not { Version: 3 or 4 or 5 or 6 }) throw new TwrpWorkflowException("Twrp_RestoreFormatRequired");
            }
            else TwrpSafety.CliBackupName(_archives.OutputPath(BackupDirectory, BackupName));
            var request = new TwrpRequest(_serial, new[] { "data" }, StorageRoot, RescueDirectory, TwrpSafety.Confirmation, StagingRoot: StagingRoot,
                Options: new(false, true, VerifyDigest: IsRestoreMode), BackupName: IsRestoreMode ? null : BackupName,
                BackupDirectory: IsRestoreMode ? null : BackupDirectory, SourceArchivePath: IsRestoreMode ? ArchiveSource : null,
                RecoveryDevice: string.IsNullOrEmpty(_marker) ? null : _marker,
                SourceImagePath: imagePath);
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
    [RelayCommand(CanExecute = nameof(CanRecover))]
    private async Task RecoverAsync(CancellationToken token)
    {
        Guid sessionId = SelectedRecovery!.Session.Id;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _configurationToken);
        IsBusy = true;
        try
        {
            if (await _confirmation.ShowConfirmationAsync(new()
            {
                Caption = _localization.GetString("Twrp_Recover"), Message = DeviceHeading + Environment.NewLine + SelectedRecovery.Label,
                WarningMessage = _localization.GetString("Twrp_SafetyLimit"),
                ConfirmButtonText = _localization.GetString("Twrp_Recover"), CancelButtonText = _localization.GetString("Twrp_Cancel"),
                Icon = ConfirmationDialogIcon.Warning
            }, lifetime.Token))
            {
                lifetime.Token.ThrowIfCancellationRequested();
                Submitted?.Invoke(new(RecoverSessionId: sessionId));
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex); }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void RetryExport() => Submitted?.Invoke(new(RetryExportSessionId: SelectedExport!.Session.Id));
    private void ShowError(Exception ex)
    {
        _logger.LogWarning(ex, "Invalid TWRP configuration for {Serial}.", _serial); Error = _localization.GetString(ex is TwrpWorkflowException known ? known.MessageKey : "Twrp_WorkflowFailed");
    }
    private void UpdateBackupSummary()
    {
        BackupSummary = _manifest is null ? "" : string.Format(_localization.GetString("Twrp_ArchiveInfoFormat"),
            _manifest.Name, _manifest.CreatedAt.ToLocalTime().ToString("g"), _manifest.Device.Codename, _manifest.Device.Serial,
            string.Join(", ", _manifest.Partitions.Select(p => _localization.GetString("Twrp_Part_" + p))));
    }
    private void LanguageChanged(object? sender, EventArgs e) { OnPropertyChanged(nameof(DeviceHeading)); UpdateBackupSummary(); foreach (var p in Partitions) p.Refresh(); }
    public void Dispose()
    {
        _localization.LanguageChanged -= LanguageChanged;
        foreach (var p in Partitions) p.PropertyChanged -= PartitionChanged;
    }
}
public sealed partial class TwrpPartitionOption(TwrpPartition partition, ILocalizationService localization, string? unavailableKey = null) : ObservableObject
{
    public TwrpPartition Partition { get; } = partition;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isAvailable = true;
    public string Label => localization.GetString("Twrp_Part_" + Partition.Id);
    public string Description => localization.GetString(unavailableKey ?? ("Twrp_PartHint_" + Partition.Id));
    public void Refresh() { OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(Description)); }
}
public sealed class TwrpSessionRow(TwrpSession session, ILocalizationService localization)
{
    public TwrpSession Session { get; } = session;
    public string Label => (Session.ExecutionMode == TwrpExecutionMode.LegacyFlashedBoot ? localization.GetString("Twrp_LegacySession") + " · " : "") + $"{Session.CreatedAt.ToLocalTime():g} · {Session.Id.ToString("N")[..8]} · {localization.GetString(Session.ExportError ?? ("Twrp_State_" + Session.State))}";
}
