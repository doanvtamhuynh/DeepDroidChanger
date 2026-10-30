using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging;
using ScrcpyNet;

namespace DeepDroidChanger.ViewModels;

public sealed partial class ViewMultipleDevicesViewModel
{
    private const int RuntimeVerificationMaxAttempts = 3;
    private static readonly TimeSpan RuntimeVerificationRetryDelay = TimeSpan.FromMilliseconds(150);

    private enum ToolAction
    {
        Power, VolumeUp, VolumeDown, Reconnect, ScreenOn, ScreenOff, Rotate,
        InputText, Shell, WifiOn, WifiOff, GmsOn, GmsOff, StoreOn, StoreOff,
        Screenshot, Import, Export, Install, Reboot
    }
    private enum ToolEditor { None, Input, Files, Screen, Wifi, Gms, PlayStore }
    private sealed class ToolVerificationException() : Exception("ViewMultipleDevices_UnableToVerify");

    private IAdbCommandService? _toolAdb;
    private IDeviceActionService? _toolActions;
    private IPackageInstallService? _toolPackages;
    private IViewDeviceScreenshotService? _toolScreenshots;
    private IFilePickerDialogService? _toolPicker;
    private IFileSystemService? _toolFiles;
    private IViewMultipleDeviceArtifactService? _toolArtifacts;
    private IDeviceActionCoordinatorService? _toolCoordinator;
    private readonly List<IRelayCommand> _toolCommands = [];
    private ToolEditor _toolEditor;
    private string _inputText = string.Empty;
    private string _adbShellCommand = string.Empty;
    private string _computerFilePath = string.Empty;
    private string _androidFilePath = "/sdcard/Download/";

    public ObservableCollection<ViewMultipleDeviceToolResultViewModel> ToolResults { get; } = [];
    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
                NotifyToolCommands();
        }
    }

    public string AdbShellCommand
    {
        get => _adbShellCommand;
        set
        {
            if (SetProperty(ref _adbShellCommand, value))
                NotifyToolCommands();
        }
    }

    public string ComputerFilePath
    {
        get => _computerFilePath;
        set
        {
            if (SetProperty(ref _computerFilePath, value))
                NotifyToolCommands();
        }
    }

    public string AndroidFilePath
    {
        get => _androidFilePath;
        set
        {
            if (SetProperty(ref _androidFilePath, value))
                NotifyToolCommands();
        }
    }

    public bool IsInputEditorOpen => _toolEditor == ToolEditor.Input;
    public bool IsFileEditorOpen => _toolEditor == ToolEditor.Files;
    public bool IsToggleEditorOpen => _toolEditor is ToolEditor.Screen or ToolEditor.Wifi or ToolEditor.Gms or ToolEditor.PlayStore;
    public bool HasToolEditor => _toolEditor != ToolEditor.None;
    public string ToolEditorTitle => _toolEditor switch
    {
        ToolEditor.Input => _localization.GetString("ViewDevice_InputAdb"),
        ToolEditor.Files => _localization.GetString("ViewDevice_ImportExport"),
        ToolEditor.Screen => _localization.GetString("ViewMultipleDevices_ScreenToggle"),
        ToolEditor.Wifi => _localization.GetString("ViewDevice_Wifi"),
        ToolEditor.Gms => _localization.GetString("ViewDevice_Gms"),
        ToolEditor.PlayStore => _localization.GetString("ViewDevice_PlayStore"),
        _ => string.Empty
    };

    public IAsyncRelayCommand PowerCommand { get; private set; } = null!;
    public IAsyncRelayCommand VolumeUpCommand { get; private set; } = null!;
    public IAsyncRelayCommand VolumeDownCommand { get; private set; } = null!;
    public IAsyncRelayCommand ReconnectCommand { get; private set; } = null!;
    public IAsyncRelayCommand RebootCommand { get; private set; } = null!;
    public IAsyncRelayCommand RotateViewsCommand { get; private set; } = null!;
    public IAsyncRelayCommand ScreenshotCommand { get; private set; } = null!;
    public IAsyncRelayCommand InstallPackageCommand { get; private set; } = null!;
    public IAsyncRelayCommand SendInputTextCommand { get; private set; } = null!;
    public IAsyncRelayCommand RunAdbShellCommand { get; private set; } = null!;
    public IAsyncRelayCommand ImportFileCommand { get; private set; } = null!;
    public IAsyncRelayCommand ExportFileCommand { get; private set; } = null!;
    public IAsyncRelayCommand EnableAllCommand { get; private set; } = null!;
    public IAsyncRelayCommand DisableAllCommand { get; private set; } = null!;
    public IRelayCommand<string> OpenToolEditorCommand { get; private set; } = null!;
    public IRelayCommand CloseToolEditorCommand { get; private set; } = null!;
    public IRelayCommand BrowseComputerFileCommand { get; private set; } = null!;
    public IRelayCommand StopToolsCommand { get; private set; } = null!;

    private void InitializeTools(IAdbCommandService? adb, IDeviceActionService? actions,
        IPackageInstallService? packages, IViewDeviceScreenshotService? screenshots,
        IFilePickerDialogService? picker, IFileSystemService? files,
        IViewMultipleDeviceArtifactService? artifacts, IDeviceActionCoordinatorService? coordinator)
    {
        _toolAdb = adb;
        _toolActions = actions;
        _toolPackages = packages;
        _toolScreenshots = screenshots;
        _toolPicker = picker;
        _toolFiles = files;
        _toolArtifacts = artifacts;
        _toolCoordinator = coordinator;
        IAsyncRelayCommand CreateToolCommand(ToolAction action)
        {
            IAsyncRelayCommand command = new AsyncRelayCommand(() => RunToolBatchAsync(action), () => CanRunTool(action));
            _toolCommands.Add(command);
            return command;
        }
        PowerCommand = CreateToolCommand(ToolAction.Power);
        VolumeUpCommand = CreateToolCommand(ToolAction.VolumeUp);
        VolumeDownCommand = CreateToolCommand(ToolAction.VolumeDown);
        ReconnectCommand = CreateToolCommand(ToolAction.Reconnect);
        RebootCommand = CreateToolCommand(ToolAction.Reboot);
        RotateViewsCommand = CreateToolCommand(ToolAction.Rotate);
        ScreenshotCommand = CreateToolCommand(ToolAction.Screenshot);
        InstallPackageCommand = CreateToolCommand(ToolAction.Install);
        SendInputTextCommand = CreateToolCommand(ToolAction.InputText);
        RunAdbShellCommand = CreateToolCommand(ToolAction.Shell);
        ImportFileCommand = CreateToolCommand(ToolAction.Import);
        ExportFileCommand = CreateToolCommand(ToolAction.Export);
        EnableAllCommand = new AsyncRelayCommand(() => RunToolBatchAsync(ToggleAction(true)), () => IsToggleEditorOpen && CanRunTool(ToggleAction(true)));
        DisableAllCommand = new AsyncRelayCommand(() => RunToolBatchAsync(ToggleAction(false)), () => IsToggleEditorOpen && CanRunTool(ToggleAction(false)));
        OpenToolEditorCommand = new RelayCommand<string>(value =>
        {
            if (Enum.TryParse(value, out ToolEditor editor)) SetToolEditor(editor);
        }, value => !IsBatchBusy && HasSelectedDevices && Enum.TryParse<ToolEditor>(value, out _));
        CloseToolEditorCommand = new RelayCommand(() => SetToolEditor(ToolEditor.None), () => !IsBatchBusy);
        BrowseComputerFileCommand = new RelayCommand(() =>
        {
            string? path = _toolPicker?.ShowOpenFileDialog(_localization.GetString("ViewDevice_FileFilter"), _localization.GetString("ViewDevice_ImportTitle"));
            if (!string.IsNullOrWhiteSpace(path)) ComputerFilePath = path;
        }, () => !IsBatchBusy && _toolPicker is not null);
        StopToolsCommand = new RelayCommand(() => CancelCancellation(_batchCancellation), () => IsBatchBusy);
        _toolCommands.AddRange([EnableAllCommand, DisableAllCommand, OpenToolEditorCommand, CloseToolEditorCommand, BrowseComputerFileCommand, StopToolsCommand]);
        _localization.LanguageChanged += OnToolsLanguageChanged;
    }

    private void SetToolEditor(ToolEditor editor)
    {
        _toolEditor = editor;
        OnPropertyChanged(nameof(IsInputEditorOpen));
        OnPropertyChanged(nameof(IsFileEditorOpen));
        OnPropertyChanged(nameof(IsToggleEditorOpen));
        OnPropertyChanged(nameof(HasToolEditor));
        OnPropertyChanged(nameof(ToolEditorTitle));
        NotifyToolCommands();
    }

    private ToolAction ToggleAction(bool enabled) => _toolEditor switch
    {
        ToolEditor.Screen => enabled ? ToolAction.ScreenOn : ToolAction.ScreenOff,
        ToolEditor.Wifi => enabled ? ToolAction.WifiOn : ToolAction.WifiOff,
        ToolEditor.Gms => enabled ? ToolAction.GmsOn : ToolAction.GmsOff,
        ToolEditor.PlayStore => enabled ? ToolAction.StoreOn : ToolAction.StoreOff,
        _ => throw new InvalidOperationException("No toggle editor is open.")
    };

    private bool CanRunTool(ToolAction action)
    {
        if (IsBatchBusy || !Volatile.Read(ref _isActive) || Volatile.Read(ref _disposed) != 0 || !HasSelectedDevices)
            return false;
        return action switch
        {
            ToolAction.Power or ToolAction.VolumeUp or ToolAction.VolumeDown => _toolAdb is not null,
            ToolAction.Reboot or ToolAction.WifiOn or ToolAction.WifiOff or ToolAction.GmsOn or ToolAction.GmsOff or ToolAction.StoreOn or ToolAction.StoreOff => _toolActions is not null,
            ToolAction.Shell => _toolAdb is not null && !string.IsNullOrWhiteSpace(AdbShellCommand),
            ToolAction.InputText => !string.IsNullOrEmpty(InputText),
            ToolAction.Screenshot => _toolPicker is not null && _toolArtifacts is not null && _toolScreenshots is not null,
            ToolAction.Install => _toolPicker is not null && _toolPackages is not null && _toolFiles is not null,
            ToolAction.Import => _toolAdb is not null && _toolFiles is not null && !string.IsNullOrWhiteSpace(AndroidFilePath),
            ToolAction.Export => _toolAdb is not null && _toolPicker is not null && _toolArtifacts is not null && !string.IsNullOrWhiteSpace(AndroidFilePath),
            _ => true
        };
    }

    private void NotifyToolCommands()
    {
        foreach (IRelayCommand command in _toolCommands) command.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BackgroundSessionCount));
    }

    private void ResetToolPresentation()
    {
        ToolResults.Clear();
        _batchCompleted = _batchTotal = 0;
        _activeToolKey = string.Empty;
        SetToolEditor(ToolEditor.None);
        OnPropertyChanged(nameof(HasToolResults));
        OnPropertyChanged(nameof(ActiveToolText));
        OnPropertyChanged(nameof(BatchSummary));
    }

    private async Task<string> ExecuteToolAsync(ToolTarget target, ToolRequest request, string runDirectory, CancellationToken token)
    {
        if (request.Action is ToolAction.Power or ToolAction.VolumeUp or ToolAction.VolumeDown or
            ToolAction.Reconnect or ToolAction.ScreenOn or ToolAction.ScreenOff or ToolAction.InputText)
            return await ExecuteToolCoreAsync(target, request, runDirectory, token).ConfigureAwait(false);
        return await target.Item.ExecuteExternalToolAsync(
            () => ExecuteToolCoreAsync(target, request, runDirectory, token), token).ConfigureAwait(false);
    }

    private async Task<string> ExecuteToolCoreAsync(ToolTarget target, ToolRequest request, string runDirectory, CancellationToken token)
    {
        ViewMultipleDeviceItemViewModel item = target.Item;
        string serial = item.Serial;
        string? unavailable = ToolUnavailableReason(item);
        if (unavailable is not null) throw new InvalidOperationException(unavailable);
        switch (request.Action)
        {
            case ToolAction.Power:
            case ToolAction.VolumeUp:
            case ToolAction.VolumeDown:
                int key = request.Action == ToolAction.Power ? 26 : request.Action == ToolAction.VolumeUp ? 24 : 25;
                await item.SendToolKeyAsync(key, ct => _toolAdb!.SendKeyEventAsync(serial, key, ct), token).ConfigureAwait(false);
                if (request.Action == ToolAction.Power)
                    await Task.Delay(ViewDeviceViewModel.PowerCooldown, CancellationToken.None).ConfigureAwait(false);
                break;
            case ToolAction.Reconnect:
                await item.ExecuteSessionToolAsync((_, _) => Task.CompletedTask, token, reconnect: true).ConfigureAwait(false);
                break;
            case ToolAction.ScreenOn:
            case ToolAction.ScreenOff:
                bool screenOn = request.Action == ToolAction.ScreenOn;
                await item.ExecuteSessionToolAsync((session, ct) => session.SetScreenPowerModeAsync(
                    screenOn ? AndroidScreenPowerMode.POWER_MODE_NORMAL : AndroidScreenPowerMode.POWER_MODE_OFF, ct), token, screenOn: screenOn).ConfigureAwait(false);
                break;
            case ToolAction.Rotate:
                await _uiDispatcher.InvokeAsync(item.RotateView, token).ConfigureAwait(false);
                break;
            case ToolAction.InputText:
                await item.ExecuteSessionToolAsync((session, ct) => session.InjectTextAsync(request.Text, ct), token).ConfigureAwait(false);
                break;
            case ToolAction.Shell:
                return DescribeCommand(await _toolAdb!.RunAdbShellAsync(serial, request.Shell, token).ConfigureAwait(false));
            case ToolAction.Reboot:
                await _toolActions!.RebootAsync(serial, token).ConfigureAwait(false);
                break;
            case ToolAction.WifiOn:
            case ToolAction.WifiOff:
                bool wifiOn = request.Action == ToolAction.WifiOn;
                await _toolActions!.SetWifiEnabledAsync(serial, wifiOn, token).ConfigureAwait(false);
                await VerifyRuntimeStateAsync(async () =>
                    await _toolActions.GetWifiEnabledAsync(serial, token).ConfigureAwait(false) == wifiOn, token).ConfigureAwait(false);
                break;
            case ToolAction.GmsOn:
            case ToolAction.GmsOff:
            case ToolAction.StoreOn:
            case ToolAction.StoreOff:
                await SetGooglePackageAsync(serial, request.Action, token).ConfigureAwait(false);
                break;
            case ToolAction.Screenshot:
                string imagePath = _toolArtifacts!.CreateDevicePath(runDirectory, target.Index, serial, "screenshot.png");
                await _toolScreenshots!.CapturePngAsync(serial, imagePath, token).ConfigureAwait(false);
                return imagePath;
            case ToolAction.Import:
                if (!_toolFiles!.FileExists(request.Source)) throw new InvalidOperationException("ViewMultipleDevices_FileMissing");
                return DescribeCommand(await _toolAdb!.PushFileAsync(serial, request.Source, request.AndroidPath, token).ConfigureAwait(false));
            case ToolAction.Export:
                string fileName = request.AndroidPath.Replace('\\', '/').TrimEnd('/').Split('/').Last();
                string destination = _toolArtifacts!.CreateDevicePath(runDirectory, target.Index, serial, fileName);
                string output = DescribeCommand(await _toolAdb!.PullFileAsync(serial, request.AndroidPath, destination, token).ConfigureAwait(false));
                return destination + Environment.NewLine + output;
            case ToolAction.Install:
                if (!_toolFiles!.FileExists(request.Source)) throw new InvalidOperationException("ViewMultipleDevices_FileMissing");
                if (!ViewDeviceViewModel.IsSupportedPackagePath(request.Source))
                    throw new InvalidOperationException("ViewMultipleDevices_UnsupportedPackage");
                using (IDeviceActionOperation? operation = _toolCoordinator?.TryStart(serial, DeviceActionKind.BatchInstallPackages,
                    canCancel: true, token, sessionId: request.SessionId, source: DeviceActionSource.MultipleDevices))
                {
                    if (_toolCoordinator is not null && operation is null) throw new InvalidOperationException("ViewMultipleDevices_ActionBusy");
                    InstallPackageResult result = await _toolPackages!.InstallAsync(serial, request.Source,
                        new InstallPackageOptions(grantPermissions: false, allowDowngrade: false), operation?.CancellationToken ?? token).ConfigureAwait(false);
                    (operation?.CancellationToken ?? token).ThrowIfCancellationRequested();
                    string message = string.Format(CultureInfo.CurrentCulture, _localization.GetString(result.MessageResourceKey), result.MessageArguments.ToArray());
                    if (!result.Success) throw new InvalidOperationException(message);
                    return message;
                }
        }
        return string.Empty;
    }

    private async Task SetGooglePackageAsync(string serial, ToolAction action, CancellationToken token)
    {
        bool gms = action is ToolAction.GmsOn or ToolAction.GmsOff;
        bool enabled = action is ToolAction.GmsOn or ToolAction.StoreOn;
        GooglePackageState before = await _toolActions!.GetGooglePackageStateAsync(serial, token).ConfigureAwait(false);
        if (gms ? !before.IsGmsInstalled : !before.IsPlayStoreInstalled)
            throw new InvalidOperationException("ViewMultipleDevices_PackageMissing");
        if (gms) await _toolActions.SetGmsEnabledAsync(serial, enabled, token).ConfigureAwait(false);
        else await _toolActions.SetPlayStoreEnabledAsync(serial, enabled, token).ConfigureAwait(false);
        await VerifyRuntimeStateAsync(async () =>
        {
            GooglePackageState after = await _toolActions.GetGooglePackageStateAsync(serial, token).ConfigureAwait(false);
            return gms ? after.IsGmsInstalled && after.IsGmsDisabled != enabled :
                after.IsPlayStoreInstalled && after.IsPlayStoreDisabled != enabled;
        }, token).ConfigureAwait(false);
    }

    private async Task VerifyRuntimeStateAsync(Func<Task<bool>> query, CancellationToken token)
    {
        for (int attempt = 0; attempt < RuntimeVerificationMaxAttempts; attempt++)
        {
            try
            {
                if (await query().ConfigureAwait(false))
                    return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogDebug(exception, "Could not verify a multiple-device runtime state.");
            }
            if (attempt + 1 < RuntimeVerificationMaxAttempts)
                await Task.Delay(RuntimeVerificationRetryDelay, token).ConfigureAwait(false);
        }
        throw new ToolVerificationException();
    }

    private string DescribeCommand(CommandResult result)
    {
        string details = string.Format(CultureInfo.CurrentCulture, _localization.GetString("ViewMultipleDevices_CommandOutput"),
            result.ExitCode, Environment.NewLine, result.StandardOutput, result.StandardError);
        if (result.ExitCode != 0) throw new InvalidOperationException(details);
        return details;
    }

    private static string ToolTitleKey(ToolAction action) => action switch
    {
        ToolAction.Power => "ViewMultipleDevices_Power",
        ToolAction.VolumeUp => "ViewDevice_VolumeUpDescription",
        ToolAction.VolumeDown => "ViewDevice_VolumeDownDescription",
        ToolAction.Reconnect => "ViewMultipleDevices_Reconnect",
        ToolAction.ScreenOn or ToolAction.ScreenOff => "ViewMultipleDevices_ScreenToggle",
        ToolAction.Rotate => "ViewDevice_RotateScreen",
        ToolAction.InputText or ToolAction.Shell => "ViewDevice_InputAdb",
        ToolAction.WifiOn or ToolAction.WifiOff => "ViewDevice_Wifi",
        ToolAction.GmsOn or ToolAction.GmsOff => "ViewDevice_Gms",
        ToolAction.StoreOn or ToolAction.StoreOff => "ViewDevice_PlayStore",
        ToolAction.Screenshot => "ViewDevice_Screenshot",
        ToolAction.Import or ToolAction.Export => "ViewDevice_ImportExport",
        ToolAction.Install => "ViewDevice_InstallPackage",
        ToolAction.Reboot => "ViewMultipleDevices_Reboot",
        _ => "ViewMultipleDevices_DeviceTools"
    };

    private async void OnToolsLanguageChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(BatchSummary));
                OnPropertyChanged(nameof(ActiveToolText));
                OnPropertyChanged(nameof(ToolEditorTitle));
                foreach (ViewMultipleDeviceToolResultViewModel result in ToolResults) result.RefreshLanguage();
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not refresh tool language.");
        }
    }
}
