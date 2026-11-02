using DeepDroidChanger.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DeepDroidChanger.ViewModels;

// Editable draft; only Save publishes these values to the application.
public sealed partial class SettingsDraftViewModel : ObservableObject
{
    public SettingsAppearanceViewModel Appearance { get; } = new();
    [ObservableProperty] private string _accountDisplayName = string.Empty;
    [ObservableProperty] private bool _rememberAccount = true;
    [ObservableProperty] private string _singleViewQuality = "Balanced";
    [ObservableProperty] private string _multiViewQuality = "Lightweight";
    [ObservableProperty] private string _keyboardMode = "Sdk";
    [ObservableProperty] private bool _syncClipboard = true;
    [ObservableProperty] private bool _enableAudio;
    [ObservableProperty] private bool _autoReconnect = true;
    [ObservableProperty] private string _maxConcurrentTools = "4";
    [ObservableProperty] private string _batchDelay = "0";
    [ObservableProperty] private bool _continueOnError = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseScreenshotFolder))]
    private string _screenshotDestination = "Ask";
    [ObservableProperty] private string _screenshotFolder = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseExportFolder))]
    private string _exportDestination = "Ask";
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _backupFolder = "Backup";

    public bool UseScreenshotFolder => ScreenshotDestination == "Custom";
    public bool UseExportFolder => ExportDestination == "Custom";

    public void Load(SettingsOptions options) => Rebase(options, options, force: true);

    public SettingsOptions Capture() => new()
    {
        AccountDisplayName = AccountDisplayName,
        RememberAccount = RememberAccount,
        SingleViewQuality = SingleViewQuality,
        MultiViewQuality = MultiViewQuality,
        KeyboardMode = KeyboardMode,
        SyncClipboard = SyncClipboard,
        EnableAudio = EnableAudio,
        AutoReconnect = AutoReconnect,
        MaxConcurrentTools = MaxConcurrentTools,
        BatchDelay = BatchDelay,
        ContinueOnError = ContinueOnError,
        ScreenshotDestination = ScreenshotDestination,
        ScreenshotFolder = ScreenshotFolder,
        ExportDestination = ExportDestination,
        ExportFolder = ExportFolder,
        BackupFolder = BackupFolder,
        Appearance = Appearance.Capture()
    };

    public void Rebase(SettingsOptions previous, SettingsOptions current, bool force = false)
    {
        if (force || AccountDisplayName == previous.AccountDisplayName) AccountDisplayName = current.AccountDisplayName;
        if (force || RememberAccount == previous.RememberAccount) RememberAccount = current.RememberAccount;
        if (force || SingleViewQuality == previous.SingleViewQuality) SingleViewQuality = current.SingleViewQuality;
        if (force || MultiViewQuality == previous.MultiViewQuality) MultiViewQuality = current.MultiViewQuality;
        if (force || KeyboardMode == previous.KeyboardMode) KeyboardMode = current.KeyboardMode;
        if (force || SyncClipboard == previous.SyncClipboard) SyncClipboard = current.SyncClipboard;
        if (force || EnableAudio == previous.EnableAudio) EnableAudio = current.EnableAudio;
        if (force || AutoReconnect == previous.AutoReconnect) AutoReconnect = current.AutoReconnect;
        if (force || MaxConcurrentTools == previous.MaxConcurrentTools) MaxConcurrentTools = current.MaxConcurrentTools;
        if (force || BatchDelay == previous.BatchDelay) BatchDelay = current.BatchDelay;
        if (force || ContinueOnError == previous.ContinueOnError) ContinueOnError = current.ContinueOnError;
        if (force || ScreenshotDestination == previous.ScreenshotDestination) ScreenshotDestination = current.ScreenshotDestination;
        if (force || ScreenshotFolder == previous.ScreenshotFolder) ScreenshotFolder = current.ScreenshotFolder;
        if (force || ExportDestination == previous.ExportDestination) ExportDestination = current.ExportDestination;
        if (force || ExportFolder == previous.ExportFolder) ExportFolder = current.ExportFolder;
        if (force || BackupFolder == previous.BackupFolder) BackupFolder = current.BackupFolder;
        Appearance.Rebase(previous.Appearance, current.Appearance, force);
    }
}
