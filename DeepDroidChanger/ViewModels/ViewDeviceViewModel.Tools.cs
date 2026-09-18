using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using ScrcpyNet;

namespace DeepDroidChanger.ViewModels;

public sealed partial class ViewDeviceViewModel
{
    private static readonly TimeSpan RuntimeStatePollingInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RuntimeVerificationRetryDelay = TimeSpan.FromMilliseconds(150);
    private const int RuntimeVerificationMaxAttempts = 3;

    private readonly object _runtimePollingLifecycleGate = new();
    private readonly SemaphoreSlim _runtimePollingTransitionGate = new(1, 1);
    private readonly SemaphoreSlim _runtimeRefreshGate = new(1, 1);

    private CancellationTokenSource? _activeRuntimePollingCancellation;
    private Task _runtimePollingTask = Task.CompletedTask;
    private long _runtimePollingCycle;
    private int _toolsVisibilityRequested;
    private int _runtimeRefreshDisposed;
    private int _renameBusy;
    private int _inputBusy;
    private int _adbShellBusy;
    private int _fileTransferBusy;
    private int _installBusy;
    private int _runtimeToggleBusy;
    private int _restartBusy;
    private long _adbShellRequestGeneration;
    private ViewDeviceToolEditor _activeToolEditor;
    private string _renameName = string.Empty;
    private string _inputText = string.Empty;
    private string _adbShellCommand = string.Empty;
    private string _computerFilePath = string.Empty;
    private string _androidFilePath = "/sdcard/Download/";
    private string _installFilePath = string.Empty;
    private string _toolStatusText = string.Empty;
    private string? _toolStatusResourceKey;
    private object[] _toolStatusArguments = [];
    private bool? _isWifiEnabled;
    private bool? _isScreenOn;
    private bool? _isGmsEnabled;
    private bool? _isPlayStoreEnabled;

    public IRelayCommand RenameConfigCommand { get; private set; } = null!;
    public IAsyncRelayCommand SaveRenameConfigCommand { get; private set; } = null!;
    public IRelayCommand InputAdbCommand { get; private set; } = null!;
    public IAsyncRelayCommand InputDeviceTextCommand { get; private set; } = null!;
    public IAsyncRelayCommand RunAdbShellCommand { get; private set; } = null!;
    public IRelayCommand RotateViewCommand { get; private set; } = null!;
    public IRelayCommand FileTransferCommand { get; private set; } = null!;
    public IAsyncRelayCommand ImportFileCommand { get; private set; } = null!;
    public IRelayCommand BrowseComputerFileCommand { get; private set; } = null!;
    public IAsyncRelayCommand PushFileCommand { get; private set; } = null!;
    public IAsyncRelayCommand ExportFileCommand { get; private set; } = null!;
    public IAsyncRelayCommand PullFileCommand { get; private set; } = null!;
    public IAsyncRelayCommand ToggleWifiCommand { get; private set; } = null!;
    public IAsyncRelayCommand ToggleScreenCommand { get; private set; } = null!;
    public IAsyncRelayCommand RebootDeviceCommand { get; private set; } = null!;
    public IAsyncRelayCommand<string?> InstallPackageCommand { get; private set; } = null!;
    public IAsyncRelayCommand ToggleGmsCommand { get; private set; } = null!;
    public IAsyncRelayCommand TogglePlayStoreCommand { get; private set; } = null!;

    public ViewDeviceToolEditor ActiveToolEditor
    {
        get => _activeToolEditor;
        private set => SetProperty(ref _activeToolEditor, value);
    }

    public bool IsDeviceOnline =>
        Volatile.Read(ref _disposed) == 0 &&
        _deviceTracker.Health == AdbDeviceTrackerHealth.Connected &&
        _deviceTracker.GetDevice(Serial)?.Status == AdbDeviceStatus.Online;

    public bool IsRuntimeToggleAvailable => IsDeviceOnline && !IsRuntimeToggleBusy;

    public bool IsWifiActionAvailable =>
        IsDeviceOnline && IsWifiEnabled.HasValue && !IsRuntimeToggleBusy;

    public bool IsGmsActionAvailable =>
        IsDeviceOnline && IsGmsEnabled.HasValue && !IsRuntimeToggleBusy;

    public bool IsPlayStoreActionAvailable =>
        IsDeviceOnline && IsPlayStoreEnabled.HasValue && !IsRuntimeToggleBusy;

    public bool IsScreenActionAvailable =>
        CanInteract() && IsScreenOn.HasValue && !IsRuntimeToggleBusy;

    public string WifiActionText =>
        GetNextStateText(
            IsWifiEnabled,
            "ChangeSingleDevice_RowMenuDisableWifi",
            "ChangeSingleDevice_RowMenuEnableWifi",
            "ViewDevice_Wifi");

    public string GmsActionText =>
        GetNextStateText(
            IsGmsEnabled,
            "ChangeSingleDevice_RowMenuDisableGms",
            "ChangeSingleDevice_RowMenuEnableGms",
            "ViewDevice_Gms");

    public string PlayStoreActionText =>
        GetNextStateText(
            IsPlayStoreEnabled,
            "ChangeSingleDevice_RowMenuDisablePlayStore",
            "ChangeSingleDevice_RowMenuEnablePlayStore",
            "ViewDevice_PlayStore");

    public bool? IsWifiEnabled
    {
        get => _isWifiEnabled;
        private set
        {
            if (!SetProperty(ref _isWifiEnabled, value))
                return;

            OnPropertyChanged(nameof(IsWifiActionAvailable));
            OnPropertyChanged(nameof(WifiActionText));
            NotifyToolCommandStateChanged();
        }
    }

    public bool? IsScreenOn
    {
        get => _isScreenOn;
        private set
        {
            if (!SetProperty(ref _isScreenOn, value))
                return;

            OnPropertyChanged(nameof(IsScreenActionAvailable));
            NotifyToolCommandStateChanged();
        }
    }

    public bool? IsGmsEnabled
    {
        get => _isGmsEnabled;
        private set
        {
            if (!SetProperty(ref _isGmsEnabled, value))
                return;

            OnPropertyChanged(nameof(IsGmsActionAvailable));
            OnPropertyChanged(nameof(GmsActionText));
            NotifyToolCommandStateChanged();
        }
    }

    public bool? IsPlayStoreEnabled
    {
        get => _isPlayStoreEnabled;
        private set
        {
            if (!SetProperty(ref _isPlayStoreEnabled, value))
                return;

            OnPropertyChanged(nameof(IsPlayStoreActionAvailable));
            OnPropertyChanged(nameof(PlayStoreActionText));
            NotifyToolCommandStateChanged();
        }
    }

    public bool IsRenameBusy
    {
        get => _renameBusy != 0;
        private set => SetBusyFlag(ref _renameBusy, value, nameof(IsRenameBusy));
    }

    public bool IsInputBusy
    {
        get => _inputBusy != 0;
        private set => SetBusyFlag(ref _inputBusy, value, nameof(IsInputBusy));
    }

    public bool IsAdbShellBusy
    {
        get => _adbShellBusy != 0;
        private set => SetBusyFlag(ref _adbShellBusy, value, nameof(IsAdbShellBusy));
    }

    public bool IsFileTransferBusy
    {
        get => _fileTransferBusy != 0;
        private set => SetBusyFlag(ref _fileTransferBusy, value, nameof(IsFileTransferBusy));
    }

    public bool IsInstallBusy
    {
        get => _installBusy != 0;
        private set => SetBusyFlag(ref _installBusy, value, nameof(IsInstallBusy));
    }

    public bool IsRuntimeToggleBusy
    {
        get => _runtimeToggleBusy != 0;
        private set
        {
            int next = value ? 1 : 0;
            if (Interlocked.Exchange(ref _runtimeToggleBusy, next) == next)
                return;

            OnPropertyChanged(nameof(IsRuntimeToggleBusy));
            OnPropertyChanged(nameof(IsRuntimeToggleAvailable));
            OnPropertyChanged(nameof(IsWifiActionAvailable));
            OnPropertyChanged(nameof(IsGmsActionAvailable));
            OnPropertyChanged(nameof(IsPlayStoreActionAvailable));
            OnPropertyChanged(nameof(IsScreenActionAvailable));
            OnPropertyChanged(nameof(IsDeviceMutationBusy));
            OnPropertyChanged(nameof(IsDeviceOperationBusy));
            OnPropertyChanged(nameof(IsToolActionBusy));
            NotifyToolCommandStateChanged();
        }
    }

    public bool IsRestartBusy
    {
        get => _restartBusy != 0;
        private set => SetBusyFlag(ref _restartBusy, value, nameof(IsRestartBusy));
    }

    public bool IsToolActionBusy =>
        IsRenameBusy ||
        IsInputBusy ||
        IsAdbShellBusy ||
        IsFileTransferBusy ||
        IsInstallBusy ||
        IsRuntimeToggleBusy ||
        IsRestartBusy;

    public bool IsDeviceMutationBusy =>
        IsFileTransferBusy ||
        IsInstallBusy ||
        IsRestartBusy;

    public bool IsDeviceOperationBusy =>
        IsAdbShellBusy ||
        IsFileTransferBusy ||
        IsInstallBusy ||
        IsRuntimeToggleBusy ||
        IsRestartBusy;

    public string RenameName
    {
        get => _renameName;
        set
        {
            if (!SetProperty(ref _renameName, value ?? string.Empty))
                return;

            SaveRenameConfigCommand?.NotifyCanExecuteChanged();
        }
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (!SetProperty(ref _inputText, value ?? string.Empty))
                return;

            InputDeviceTextCommand?.NotifyCanExecuteChanged();
        }
    }

    public string AdbShellCommand
    {
        get => _adbShellCommand;
        set
        {
            if (!SetProperty(ref _adbShellCommand, value ?? string.Empty))
                return;

            RunAdbShellCommand?.NotifyCanExecuteChanged();
        }
    }

    public string ComputerFilePath
    {
        get => _computerFilePath;
        set
        {
            if (!SetProperty(ref _computerFilePath, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(IsComputerFilePathValid));
            OnPropertyChanged(nameof(IsFileTransferReady));
            BrowseComputerFileCommand?.NotifyCanExecuteChanged();
            ImportFileCommand?.NotifyCanExecuteChanged();
            PushFileCommand?.NotifyCanExecuteChanged();
        }
    }

    public string AndroidFilePath
    {
        get => _androidFilePath;
        set
        {
            if (!SetProperty(ref _androidFilePath, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(IsFileTransferReady));
            ImportFileCommand?.NotifyCanExecuteChanged();
            PushFileCommand?.NotifyCanExecuteChanged();
            ExportFileCommand?.NotifyCanExecuteChanged();
            PullFileCommand?.NotifyCanExecuteChanged();
        }
    }

    public bool IsComputerFilePathValid =>
        !string.IsNullOrWhiteSpace(ComputerFilePath) &&
        File.Exists(ComputerFilePath);

    public bool IsFileTransferReady =>
        !string.IsNullOrWhiteSpace(AndroidFilePath) &&
        (!string.IsNullOrWhiteSpace(ComputerFilePath) || IsDeviceOnline);

    public string InstallFilePath
    {
        get => _installFilePath;
        private set
        {
            if (!SetProperty(ref _installFilePath, value))
                return;

            InstallPackageCommand?.NotifyCanExecuteChanged();
        }
    }

    public string ToolStatusText
    {
        get => _toolStatusText;
        private set
        {
            if (!SetProperty(ref _toolStatusText, value))
                return;

            OnPropertyChanged(nameof(HasToolStatus));
        }
    }

    public bool HasToolStatus => !string.IsNullOrWhiteSpace(ToolStatusText);

    private void InitializeToolCommands()
    {
        RenameConfigCommand = new RelayCommand(OpenRenameEditor, CanOpenRenameEditor);
        SaveRenameConfigCommand = new AsyncRelayCommand(SaveRenameConfigAsync, CanSaveRenameConfig);
        InputAdbCommand = new RelayCommand(OpenInputEditor, CanOpenOnlineEditor);
        InputDeviceTextCommand = new AsyncRelayCommand(SendInputTextAsync, CanSendInputText);
        RunAdbShellCommand = new AsyncRelayCommand(RunAdbShellAsync, CanRunAdbShell);
        RotateViewCommand = new RelayCommand(RotateViewClockwise, CanRotateView);
        FileTransferCommand = new RelayCommand(OpenFileTransferEditor, CanOpenOnlineEditor);
        BrowseComputerFileCommand = new RelayCommand(BrowseComputerFile);
        ImportFileCommand = new AsyncRelayCommand(PushFileAsync, CanPushFile);
        PushFileCommand = new AsyncRelayCommand(PushFileAsync, CanPushFile);
        ExportFileCommand = new AsyncRelayCommand(PullFileAsync, CanPullFile);
        PullFileCommand = new AsyncRelayCommand(PullFileAsync, CanPullFile);
        ToggleWifiCommand = new AsyncRelayCommand(ToggleWifiAsync, CanToggleWifiState);
        ToggleScreenCommand = new AsyncRelayCommand(ToggleScreenAsync, CanToggleScreenState);
        RebootDeviceCommand = new AsyncRelayCommand(RebootDeviceAsync, CanRebootDevice);
        InstallPackageCommand = new AsyncRelayCommand<string?>(InstallPackageAsync, CanInstallPackage);
        ToggleGmsCommand = new AsyncRelayCommand(ToggleGmsAsync, CanToggleGmsState);
        TogglePlayStoreCommand = new AsyncRelayCommand(TogglePlayStoreAsync, CanTogglePlayStoreState);
    }

    private void SetBusyFlag(ref int field, bool value, string propertyName)
    {
        int next = value ? 1 : 0;
        if (Interlocked.Exchange(ref field, next) == next)
            return;

        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(IsDeviceMutationBusy));
        OnPropertyChanged(nameof(IsDeviceOperationBusy));
        OnPropertyChanged(nameof(IsToolActionBusy));
        NotifyToolCommandStateChanged();
    }

    private void NotifyToolCommandStateChanged()
    {
        RenameConfigCommand?.NotifyCanExecuteChanged();
        SaveRenameConfigCommand?.NotifyCanExecuteChanged();
        InputAdbCommand?.NotifyCanExecuteChanged();
        InputDeviceTextCommand?.NotifyCanExecuteChanged();
        RunAdbShellCommand?.NotifyCanExecuteChanged();
        RotateViewCommand?.NotifyCanExecuteChanged();
        FileTransferCommand?.NotifyCanExecuteChanged();
        ImportFileCommand?.NotifyCanExecuteChanged();
        BrowseComputerFileCommand?.NotifyCanExecuteChanged();
        PushFileCommand?.NotifyCanExecuteChanged();
        ExportFileCommand?.NotifyCanExecuteChanged();
        PullFileCommand?.NotifyCanExecuteChanged();
        ToggleWifiCommand?.NotifyCanExecuteChanged();
        ToggleScreenCommand?.NotifyCanExecuteChanged();
        RebootDeviceCommand?.NotifyCanExecuteChanged();
        InstallPackageCommand?.NotifyCanExecuteChanged();
        ToggleGmsCommand?.NotifyCanExecuteChanged();
        TogglePlayStoreCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyToolAvailabilityChanged()
    {
        try
        {
            if (_uiDispatcher.CheckAccess())
            {
                OnPropertyChanged(nameof(IsDeviceOnline));
                OnPropertyChanged(nameof(IsRuntimeToggleAvailable));
                OnPropertyChanged(nameof(IsWifiActionAvailable));
                OnPropertyChanged(nameof(IsGmsActionAvailable));
                OnPropertyChanged(nameof(IsPlayStoreActionAvailable));
                OnPropertyChanged(nameof(IsScreenActionAvailable));
                NotifyToolCommandStateChanged();
                return;
            }

            _ = _uiDispatcher.InvokeAsync(
                () =>
                {
                    OnPropertyChanged(nameof(IsDeviceOnline));
                    OnPropertyChanged(nameof(IsRuntimeToggleAvailable));
                    OnPropertyChanged(nameof(IsWifiActionAvailable));
                    OnPropertyChanged(nameof(IsGmsActionAvailable));
                    OnPropertyChanged(nameof(IsPlayStoreActionAvailable));
                    OnPropertyChanged(nameof(IsScreenActionAvailable));
                    NotifyToolCommandStateChanged();
                },
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "View Device tool availability notification failed for {Serial}.",
                Serial);
        }
    }

    private bool CanOpenRenameEditor() =>
        Volatile.Read(ref _disposed) == 0 &&
        !IsRenameBusy &&
        !string.IsNullOrWhiteSpace(Serial);

    private bool CanOpenOnlineEditor() =>
        Volatile.Read(ref _disposed) == 0 &&
        IsDeviceOnline;

    private void OnViewDeviceStateChangedForTools(ViewDeviceSessionState state)
    {
        if (IsDeviceOnline)
            QueueRuntimeStateRefresh();
        else
            QueueRuntimeStateClear();

        NotifyToolAvailabilityChanged();
    }

    private void ClearRuntimeStateValues()
    {
        IsWifiEnabled = null;
        IsScreenOn = null;
        IsGmsEnabled = null;
        IsPlayStoreEnabled = null;
    }

    private void QueueRuntimeStateClear()
    {
        try
        {
            if (_uiDispatcher.CheckAccess())
            {
                ClearRuntimeStateValues();
                return;
            }

            _ = _uiDispatcher.InvokeAsync(ClearRuntimeStateValues, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "View Device runtime state clear notification failed for {Serial}.",
                Serial);
        }
    }

    public void SetToolsVisibility(bool isVisible)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _ = SetToolsVisibilityCoreAsync(isVisible);
    }

    internal Task SetToolsVisibilityForTestingAsync(bool isVisible)
    {
        return SetToolsVisibilityCoreAsync(isVisible);
    }

    private async Task SetToolsVisibilityCoreAsync(bool isVisible)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Volatile.Read(ref _runtimeRefreshDisposed) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _toolsVisibilityRequested, isVisible ? 1 : 0);
        if (!isVisible)
        {
            CancellationTokenSource? activeCancellation;
            lock (_runtimePollingLifecycleGate)
                activeCancellation = _activeRuntimePollingCancellation;
            CancelRuntimePolling(activeCancellation);
        }

        try
        {
            await ReconcileRuntimePollingAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "View Device runtime monitoring transition failed for {Serial}.",
                Serial);
        }
    }

    private async Task ReconcileRuntimePollingAsync()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Volatile.Read(ref _runtimeRefreshDisposed) != 0)
        {
            return;
        }

        await _runtimePollingTransitionGate
            .WaitAsync(_lifetimeCancellation.Token)
            .ConfigureAwait(false);
        try
        {
            while (true)
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    Volatile.Read(ref _runtimeRefreshDisposed) != 0)
                {
                    return;
                }

                bool requested = Volatile.Read(ref _toolsVisibilityRequested) != 0;
                CancellationTokenSource? activeCancellation;
                lock (_runtimePollingLifecycleGate)
                    activeCancellation = _activeRuntimePollingCancellation;

                if (!requested)
                {
                    if (activeCancellation is null)
                        return;

                    await StopRuntimePollingCycleAsync(activeCancellation).ConfigureAwait(false);
                    continue;
                }

                if (activeCancellation is not null)
                {
                    if (!activeCancellation.IsCancellationRequested)
                        return;

                    await StopRuntimePollingCycleAsync(activeCancellation).ConfigureAwait(false);
                    continue;
                }

                CancellationTokenSource cycleCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
                long cycle;
                lock (_runtimePollingLifecycleGate)
                {
                    cycle = ++_runtimePollingCycle;
                    _activeRuntimePollingCancellation = cycleCancellation;
                    _runtimePollingTask = Task.CompletedTask;
                }

                await RefreshRuntimeStateAsync(cycleCancellation.Token, cycle)
                    .ConfigureAwait(false);

                bool shouldStartPolling;
                lock (_runtimePollingLifecycleGate)
                {
                    shouldStartPolling =
                        ReferenceEquals(_activeRuntimePollingCancellation, cycleCancellation) &&
                        Volatile.Read(ref _toolsVisibilityRequested) != 0 &&
                        Volatile.Read(ref _disposed) == 0 &&
                        Volatile.Read(ref _runtimeRefreshDisposed) == 0 &&
                        !cycleCancellation.IsCancellationRequested;
                }

                if (!shouldStartPolling)
                {
                    await StopRuntimePollingCycleAsync(cycleCancellation).ConfigureAwait(false);
                    continue;
                }

                Task pollingTask;
                try
                {
                    pollingTask = _pollingService.RunAsync(
                        RuntimeStatePollingInterval,
                        cancellationToken => RefreshRuntimeStateAsync(cancellationToken, cycle),
                        cycleCancellation.Token) ?? Task.CompletedTask;
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(
                        exception,
                        "View Device runtime polling could not start for {Serial}.",
                        Serial);
                    await StopRuntimePollingCycleAsync(cycleCancellation).ConfigureAwait(false);
                    return;
                }

                lock (_runtimePollingLifecycleGate)
                {
                    if (ReferenceEquals(_activeRuntimePollingCancellation, cycleCancellation))
                        _runtimePollingTask = pollingTask;
                }

                return;
            }
        }
        finally
        {
            _runtimePollingTransitionGate.Release();
        }
    }

    private async Task StopRuntimePollingAsync()
    {
        if (Interlocked.Exchange(ref _runtimeRefreshDisposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _toolsVisibilityRequested, 0);
        CancellationTokenSource? activeCancellation;
        lock (_runtimePollingLifecycleGate)
            activeCancellation = _activeRuntimePollingCancellation;
        CancelRuntimePolling(activeCancellation);

        try
        {
            await _runtimePollingTransitionGate
                .WaitAsync(CancellationToken.None)
                .ConfigureAwait(true);
            try
            {
                activeCancellation = null;
                lock (_runtimePollingLifecycleGate)
                    activeCancellation = _activeRuntimePollingCancellation;
                if (activeCancellation is not null)
                    await StopRuntimePollingCycleAsync(activeCancellation).ConfigureAwait(true);
            }
            finally
            {
                _runtimePollingTransitionGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await _runtimeRefreshGate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
            _runtimeRefreshGate.Release();
        }
        catch (ObjectDisposedException)
        {
        }

        _runtimeRefreshGate.Dispose();
    }

    private async Task StopRuntimePollingCycleAsync(CancellationTokenSource cycleCancellation)
    {
        Task pollingTask;
        lock (_runtimePollingLifecycleGate)
        {
            if (!ReferenceEquals(_activeRuntimePollingCancellation, cycleCancellation))
                return;

            _activeRuntimePollingCancellation = null;
            pollingTask = _runtimePollingTask;
            _runtimePollingTask = Task.CompletedTask;
        }

        CancelRuntimePolling(cycleCancellation);
        try
        {
            await pollingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "View Device runtime polling stopped with an error for {Serial}.",
                Serial);
        }
        finally
        {
            cycleCancellation.Dispose();
        }
    }

    private static void CancelRuntimePolling(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void QueueRuntimeStateRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Volatile.Read(ref _runtimeRefreshDisposed) != 0)
        {
            return;
        }

        CancellationTokenSource? activeCancellation;
        long cycle;
        lock (_runtimePollingLifecycleGate)
        {
            activeCancellation = _activeRuntimePollingCancellation;
            cycle = _runtimePollingCycle;
            if (Volatile.Read(ref _toolsVisibilityRequested) == 0 ||
                activeCancellation is null ||
                activeCancellation.IsCancellationRequested)
            {
                return;
            }
        }

        _ = RefreshRuntimeStateAsync(activeCancellation.Token, cycle);
    }

    private async Task RefreshVisibleRuntimeStateAsync()
    {
        CancellationTokenSource? activeCancellation;
        long cycle;
        lock (_runtimePollingLifecycleGate)
        {
            activeCancellation = _activeRuntimePollingCancellation;
            cycle = _runtimePollingCycle;
            if (Volatile.Read(ref _toolsVisibilityRequested) == 0 ||
                activeCancellation is null ||
                activeCancellation.IsCancellationRequested)
            {
                return;
            }
        }

        await RefreshRuntimeStateAsync(activeCancellation.Token, cycle).ConfigureAwait(false);
    }

    private async Task RefreshRuntimeStateAsync(
        CancellationToken cancellationToken,
        long pollingCycle = 0)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Volatile.Read(ref _runtimeRefreshDisposed) != 0 ||
            !IsCurrentRuntimeRefresh(pollingCycle, cancellationToken))
        {
            return;
        }

        try
        {
            await _runtimeRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int expectedGeneration = Volatile.Read(ref _generation);
                if (!CanPollRuntimeState())
                {
                    if (IsCurrentRuntimeRefresh(pollingCycle, cancellationToken))
                    {
                        await _uiDispatcher
                            .InvokeAsync(ClearRuntimeStateValues, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    return;
                }

                Task<bool?> wifiTask = QueryWifiStateAsync(cancellationToken);
                Task<bool?> screenTask = QueryScreenStateAsync(cancellationToken);
                Task<GooglePackageState?> packageTask = QueryGooglePackageStateAsync(cancellationToken);
                await Task.WhenAll(wifiTask, screenTask, packageTask).ConfigureAwait(false);

                if (!CanPollRuntimeState() ||
                    !IsCurrentRuntimeRefresh(pollingCycle, cancellationToken) ||
                    expectedGeneration != Volatile.Read(ref _generation))
                {
                    return;
                }

                bool? wifi = wifiTask.Result;
                bool? screen = screenTask.Result;
                GooglePackageState? packages = packageTask.Result;
                await _uiDispatcher
                    .InvokeAsync(
                        () =>
                        {
                            if (!CanPollRuntimeState() ||
                                !IsCurrentRuntimeRefresh(pollingCycle, cancellationToken) ||
                                expectedGeneration != Volatile.Read(ref _generation))
                            {
                                return;
                            }

                            IsWifiEnabled = wifi;
                            IsScreenOn = screen;
                            IsGmsEnabled = packages?.IsGmsInstalled == true
                                ? !packages.IsGmsDisabled
                                : null;
                            IsPlayStoreEnabled = packages?.IsPlayStoreInstalled == true
                                ? !packages.IsPlayStoreDisabled
                                : null;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _runtimeRefreshGate.Release();
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            _lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _runtimeRefreshDisposed) != 0)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "View Device runtime state refresh failed for {Serial}.", Serial);
        }
    }

    private bool CanPollRuntimeState()
    {
        return Volatile.Read(ref _disposed) == 0 &&
               !_lifetimeCancellation.IsCancellationRequested &&
               IsDeviceOnline;
    }

    private bool IsCurrentRuntimeRefresh(long pollingCycle, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            _lifetimeCancellation.IsCancellationRequested ||
            cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (pollingCycle == 0)
            return true;

        lock (_runtimePollingLifecycleGate)
        {
            return Volatile.Read(ref _toolsVisibilityRequested) != 0 &&
                   _runtimePollingCycle == pollingCycle &&
                   _activeRuntimePollingCancellation is { } activeCancellation &&
                   activeCancellation.Token == cancellationToken &&
                   !activeCancellation.IsCancellationRequested;
        }
    }

    private async Task<bool?> QueryWifiStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _deviceActionService
                .GetWifiEnabledAsync(Serial, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Unable to query Wi-Fi state for {Serial}.", Serial);
            return null;
        }
    }

    private async Task<bool?> QueryScreenStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _deviceActionService
                .GetScreenOnAsync(Serial, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Unable to query screen state for {Serial}.", Serial);
            return null;
        }
    }

    private async Task<GooglePackageState?> QueryGooglePackageStateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _deviceActionService
                .GetGooglePackageStateAsync(Serial, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Unable to query Google package state for {Serial}.", Serial);
            return null;
        }
    }

    private void OpenRenameEditor()
    {
        RenameName = DeviceName;
        SetActiveToolEditor(ViewDeviceToolEditor.Rename);
    }

    private bool CanSaveRenameConfig()
    {
        return _deviceConfigService is not null &&
               !string.IsNullOrWhiteSpace(Serial) &&
               !IsRenameBusy &&
               !string.IsNullOrWhiteSpace(RenameName);
    }

    private async Task SaveRenameConfigAsync()
    {
        string normalizedName = RenameName.Trim();
        if (normalizedName.Length == 0 || IsRenameBusy)
        {
            SaveRenameConfigCommand.NotifyCanExecuteChanged();
            return;
        }

        IsRenameBusy = true;
        try
        {
            bool updated = await _deviceConfigService
                .RenameDeviceAsync(Serial, normalizedName, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            if (!updated)
            {
                SetToolStatus("ViewDevice_RenameFailed");
                return;
            }

            DeviceName = normalizedName;
            RenameName = normalizedName;
            SetToolStatus("ViewDevice_RenameSaved");
            SetActiveToolEditor(ViewDeviceToolEditor.None);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to rename stored View Device {Serial}.", Serial);
            SetToolStatus("ViewDevice_RenameFailed");
        }
        finally
        {
            IsRenameBusy = false;
        }
    }

    private void OpenInputEditor() => SetActiveToolEditor(ViewDeviceToolEditor.InputAdb);

    private void OpenFileTransferEditor() => SetActiveToolEditor(ViewDeviceToolEditor.FileTransfer);

    internal void CloseToolEditor() => SetActiveToolEditor(ViewDeviceToolEditor.None);

    private void SetActiveToolEditor(ViewDeviceToolEditor editor)
    {
        ActiveToolEditor = editor;
        if (editor != ViewDeviceToolEditor.None)
            ClearToolStatus();
    }

    private bool CanSendInputText()
    {
        return !IsInputBusy &&
               InputText.Length > 0 &&
               CanInteract();
    }

    private async Task SendInputTextAsync()
    {
        string text = InputText;
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null || text.Length == 0 || IsInputBusy)
        {
            InputDeviceTextCommand.NotifyCanExecuteChanged();
            return;
        }

        IsInputBusy = true;
        try
        {
            if (!ReferenceEquals(session, GetInteractiveSession()))
                return;

            await session.InjectTextAsync(text, _lifetimeCancellation.Token).ConfigureAwait(true);
            if (ReferenceEquals(session, GetInteractiveSession()))
                SetToolStatus("ViewDevice_InputSent");
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device input text failed for {Serial}.", Serial);
            SetToolStatus("ViewDevice_InputFailed");
        }
        finally
        {
            IsInputBusy = false;
        }
    }

    private bool CanRunAdbShell()
    {
        return !IsAdbShellBusy &&
               !IsRestartBusy &&
               AdbShellCommand.Trim().Length > 0 &&
               IsDeviceOnline;
    }

    private Task RunAdbShellAsync()
    {
        return RunAdbShellCoreAsync(AdbShellCommand, bypassBusy: false);
    }

    internal Task RunAdbShellForTestingAsync(string command)
    {
        return RunAdbShellCoreAsync(command, bypassBusy: true);
    }

    private async Task RunAdbShellCoreAsync(string? command, bool bypassBusy)
    {
        string normalizedCommand = command?.Trim() ?? string.Empty;
        if (normalizedCommand.Length == 0)
        {
            SetToolStatus("ViewDevice_AdbShellEmpty");
            return;
        }

        if (!IsDeviceOnline ||
            IsRestartBusy ||
            (!bypassBusy && IsAdbShellBusy))
        {
            RunAdbShellCommand.NotifyCanExecuteChanged();
            return;
        }

        if (!bypassBusy)
            IsAdbShellBusy = true;

        long requestId = Interlocked.Increment(ref _adbShellRequestGeneration);
        try
        {
            CommandResult result = await _adbCommandService
                .RunAdbShellAsync(Serial, normalizedCommand, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            if (requestId != Volatile.Read(ref _adbShellRequestGeneration))
                return;

            SetToolStatus(
                result.ExitCode == 0
                    ? "ViewDevice_AdbShellCompleted"
                    : "ViewDevice_AdbShellFailedFormat",
                result.ExitCode);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (requestId == Volatile.Read(ref _adbShellRequestGeneration))
                SetToolStatus("ViewDevice_AdbShellFailed");

            _logger.LogWarning(exception, "ADB shell command failed for {Serial}.", Serial);
        }
        finally
        {
            if (!bypassBusy)
                IsAdbShellBusy = false;
        }
    }

    private void RotateViewClockwise()
    {
        if (!CanRotateView())
            return;

        ViewRotationQuarterTurns = NextViewRotation(ViewRotationQuarterTurns);
        SetToolStatus("ViewDevice_ViewRotated");
    }

    private bool CanRotateView() => ContentWidth > 0 && ContentHeight > 0;

    private void BrowseComputerFile()
    {
        string? path = _filePicker.ShowOpenFileDialog(
            _localization.GetString("ViewDevice_FileFilter"),
            _localization.GetString("ViewDevice_ImportTitle"));
        if (!string.IsNullOrWhiteSpace(path))
            ComputerFilePath = path;
    }

    private bool CanPushFile()
    {
        return !IsFileTransferBusy &&
               !IsRestartBusy &&
               IsDeviceOnline &&
               !string.IsNullOrWhiteSpace(AndroidFilePath);
    }

    private async Task PushFileAsync()
    {
        if (!CanPushFile() || IsFileTransferBusy)
        {
            PushFileCommand.NotifyCanExecuteChanged();
            return;
        }

        if (!File.Exists(ComputerFilePath))
        {
            BrowseComputerFile();
            if (!File.Exists(ComputerFilePath))
                return;
        }

        IsFileTransferBusy = true;
        try
        {
            CommandResult result = await _adbCommandService
                .PushFileAsync(
                    Serial,
                    ComputerFilePath,
                    AndroidFilePath.Trim(),
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            SetToolStatus(
                result.ExitCode == 0
                    ? "ViewDevice_FileTransferSucceeded"
                    : "ViewDevice_FileTransferFailedFormat",
                result.ExitCode);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device file push failed for {Serial}.", Serial);
            SetToolStatus("ViewDevice_FileTransferFailed");
        }
        finally
        {
            IsFileTransferBusy = false;
        }
    }

    private string? BrowseExportFile()
    {
        string defaultName = Path.GetFileName(AndroidFilePath.TrimEnd('/', '\\'));
        if (string.IsNullOrWhiteSpace(defaultName))
            defaultName = "device-file";

        string? path = _filePicker.ShowSaveFileDialog(
            _localization.GetString("ViewDevice_FileFilter"),
            _localization.GetString("ViewDevice_ExportTitle"),
            defaultName);
        if (!string.IsNullOrWhiteSpace(path))
            ComputerFilePath = path;

        return path;
    }

    private bool CanPullFile()
    {
        return !IsFileTransferBusy &&
               !IsRestartBusy &&
               IsDeviceOnline &&
               !string.IsNullOrWhiteSpace(AndroidFilePath);
    }

    private async Task PullFileAsync()
    {
        if (!CanPullFile() || IsFileTransferBusy)
        {
            PullFileCommand.NotifyCanExecuteChanged();
            return;
        }

        if (string.IsNullOrWhiteSpace(ComputerFilePath))
        {
            BrowseExportFile();
            if (string.IsNullOrWhiteSpace(ComputerFilePath))
                return;
        }

        IsFileTransferBusy = true;
        try
        {
            CommandResult result = await _adbCommandService
                .PullFileAsync(
                    Serial,
                    AndroidFilePath.Trim(),
                    ComputerFilePath,
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            SetToolStatus(
                result.ExitCode == 0
                    ? "ViewDevice_FileTransferSucceeded"
                    : "ViewDevice_FileTransferFailedFormat",
                result.ExitCode);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device file pull failed for {Serial}.", Serial);
            SetToolStatus("ViewDevice_FileTransferFailed");
        }
        finally
        {
            IsFileTransferBusy = false;
        }
    }

    private bool CanToggleWifiState() =>
        IsWifiActionAvailable &&
        !IsRestartBusy;

    private bool CanToggleGmsState() =>
        IsGmsActionAvailable &&
        !IsRestartBusy;

    private bool CanTogglePlayStoreState() =>
        IsPlayStoreActionAvailable &&
        !IsRestartBusy;

    private bool CanToggleScreenState() =>
        IsScreenActionAvailable &&
        !IsRestartBusy;

    private async Task ToggleWifiAsync()
    {
        if (!CanToggleWifiState())
            return;

        bool? current = await QueryWifiStateAsync(_lifetimeCancellation.Token)
            .ConfigureAwait(true);
        IsWifiEnabled = current;
        if (!current.HasValue)
        {
            SetToolStatus("ViewDevice_RuntimeToggleUnableToVerify");
            return;
        }

        bool desired = !current.Value;
        await ToggleRuntimeStateAsync(
                desired,
                cancellationToken =>
                    _deviceActionService.SetWifiEnabledAsync(
                        Serial,
                        desired,
                        cancellationToken),
                QueryWifiStateAsync,
                actual => IsWifiEnabled = actual,
                "ViewDevice_RuntimeToggleSucceeded",
                "ViewDevice_RuntimeToggleFailed")
            .ConfigureAwait(true);
    }

    private async Task ToggleScreenAsync()
    {
        if (!CanToggleScreenState())
            return;

        bool? current = await QueryScreenStateAsync(_lifetimeCancellation.Token)
            .ConfigureAwait(true);
        IsScreenOn = current;
        if (!current.HasValue)
        {
            SetToolStatus("ViewDevice_RuntimeToggleUnableToVerify");
            return;
        }

        bool desired = !current.Value;
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null || !CanInteract())
            return;

        if (IsRuntimeToggleBusy)
            return;

        IsRuntimeToggleBusy = true;
        try
        {
            bool? actual = await ExecuteAndVerifyToggleAsync(
                    desired,
                    cancellationToken =>
                        session.SetScreenPowerModeAsync(
                            desired
                                ? AndroidScreenPowerMode.POWER_MODE_NORMAL
                                : AndroidScreenPowerMode.POWER_MODE_OFF,
                            cancellationToken),
                    QueryScreenStateAsync,
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            IsScreenOn = actual;
            SetToggleVerificationStatus(actual, desired);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device screen toggle failed for {Serial}.", Serial);
            SetToolStatus("ViewDevice_RuntimeToggleFailed");
        }
        finally
        {
            await RefreshRuntimeStateAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            IsRuntimeToggleBusy = false;
        }
    }

    private async Task ToggleRuntimeStateAsync(
        bool desired,
        Func<CancellationToken, Task> action,
        Func<CancellationToken, Task<bool?>> query,
        Action<bool?> applyState,
        string successKey,
        string failureKey)
    {
        if (!IsDeviceOnline || IsRestartBusy || IsRuntimeToggleBusy)
            return;

        IsRuntimeToggleBusy = true;
        try
        {
            bool? actual = await ExecuteAndVerifyToggleAsync(
                    desired,
                    action,
                    query,
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            applyState(actual);
            SetToggleVerificationStatus(actual, desired, successKey, failureKey);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device runtime toggle failed for {Serial}.", Serial);
            SetToolStatus(failureKey);
        }
        finally
        {
            await RefreshRuntimeStateAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            IsRuntimeToggleBusy = false;
        }
    }

    private async Task<bool?> ExecuteAndVerifyToggleAsync(
        bool desired,
        Func<CancellationToken, Task> action,
        Func<CancellationToken, Task<bool?>> query,
        CancellationToken cancellationToken)
    {
        await action(cancellationToken).ConfigureAwait(true);

        bool? actual = null;
        for (int attempt = 0; attempt < RuntimeVerificationMaxAttempts; attempt++)
        {
            actual = await query(cancellationToken).ConfigureAwait(true);
            if (actual.HasValue && actual.Value == desired)
                return actual;

            if (attempt + 1 < RuntimeVerificationMaxAttempts)
            {
                await _runtimeVerificationDelay(
                        RuntimeVerificationRetryDelay,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
        }

        return actual;
    }

    private void SetToggleVerificationStatus(
        bool? actual,
        bool desired,
        string successKey = "ViewDevice_RuntimeToggleSucceeded",
        string failureKey = "ViewDevice_RuntimeToggleFailed")
    {
        if (actual.HasValue && actual.Value == desired)
        {
            SetToolStatus(successKey);
            return;
        }

        SetToolStatus(
            actual.HasValue
                ? "ViewDevice_RuntimeToggleVerificationFailed"
                : "ViewDevice_RuntimeToggleUnableToVerify");
    }

    private bool IsOtherDeviceOperationBusy =>
        IsAdbShellBusy ||
        IsFileTransferBusy ||
        IsInstallBusy ||
        IsRuntimeToggleBusy;

    private bool CanRebootDevice() =>
        IsDeviceOnline &&
        !IsRestartBusy &&
        !IsOtherDeviceOperationBusy;

    private async Task RebootDeviceAsync()
    {
        if (!CanRebootDevice() || IsRestartBusy)
        {
            RebootDeviceCommand.NotifyCanExecuteChanged();
            return;
        }

        IsRestartBusy = true;
        try
        {
            await _deviceActionService
                .RebootAsync(Serial, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            SetToolStatus("ViewDevice_RestartSent");
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device reboot failed for {Serial}.", Serial);
            SetToolStatus("ViewDevice_RestartFailed");
        }
        finally
        {
            IsRestartBusy = false;
        }
    }

    private bool CanInstallPackage(string? _) =>
        _packageInstallService is not null &&
        IsDeviceOnline &&
        !IsInstallBusy &&
        !IsRestartBusy;

    private async Task InstallPackageAsync(string? suppliedPath)
    {
        string? path = string.IsNullOrWhiteSpace(suppliedPath)
            ? InstallFilePath
            : suppliedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = _filePicker.ShowOpenFileDialog(
                _localization.GetString("ViewDevice_InstallFilter"),
                _localization.GetString("ViewDevice_InstallTitle"));
        }

        if (!IsSupportedPackagePath(path))
        {
            SetToolStatus("ViewDevice_InstallFileInvalid");
            return;
        }

        if (!CanInstallPackage(path) || IsInstallBusy)
        {
            InstallPackageCommand.NotifyCanExecuteChanged();
            return;
        }

        InstallFilePath = path!;
        IsInstallBusy = true;
        try
        {
            InstallPackageResult result = await _packageInstallService
                .InstallAsync(
                    Serial,
                    path!,
                    new InstallPackageOptions(
                        grantPermissions: false,
                        allowDowngrade: false),
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            SetToolStatus(
                result.MessageResourceKey,
                result.MessageArguments.ToArray());
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device package installation failed for {Serial}.", Serial);
            SetToolStatus("ViewDevice_InstallFailed");
        }
        finally
        {
            IsInstallBusy = false;
        }
    }

    private async Task ToggleGmsAsync()
    {
        if (!CanToggleGmsState())
            return;

        bool? current = await QueryGmsEnabledAsync(_lifetimeCancellation.Token)
            .ConfigureAwait(true);
        IsGmsEnabled = current;
        if (!current.HasValue)
        {
            SetToolStatus("ViewDevice_RuntimeToggleUnableToVerify");
            return;
        }

        bool desired = !current.Value;
        await ToggleRuntimeStateAsync(
                desired,
                cancellationToken =>
                    _deviceActionService.SetGmsEnabledAsync(
                        Serial,
                        desired,
                        cancellationToken),
                QueryGmsEnabledAsync,
                actual => IsGmsEnabled = actual,
                "ViewDevice_RuntimeToggleSucceeded",
                "ViewDevice_RuntimeToggleFailed")
            .ConfigureAwait(true);
    }

    private async Task TogglePlayStoreAsync()
    {
        if (!CanTogglePlayStoreState())
            return;

        bool? current = await QueryPlayStoreEnabledAsync(_lifetimeCancellation.Token)
            .ConfigureAwait(true);
        IsPlayStoreEnabled = current;
        if (!current.HasValue)
        {
            SetToolStatus("ViewDevice_RuntimeToggleUnableToVerify");
            return;
        }

        bool desired = !current.Value;
        await ToggleRuntimeStateAsync(
                desired,
                cancellationToken =>
                    _deviceActionService.SetPlayStoreEnabledAsync(
                        Serial,
                        desired,
                        cancellationToken),
                QueryPlayStoreEnabledAsync,
                actual => IsPlayStoreEnabled = actual,
                "ViewDevice_RuntimeToggleSucceeded",
                "ViewDevice_RuntimeToggleFailed")
            .ConfigureAwait(true);
    }

    internal static bool IsSupportedPackagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".apk", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".xapk", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool?> QueryGmsEnabledAsync(CancellationToken cancellationToken)
    {
        GooglePackageState? state = await QueryGooglePackageStateAsync(cancellationToken)
            .ConfigureAwait(true);
        return state?.IsGmsInstalled == true
            ? !state.IsGmsDisabled
            : null;
    }

    private async Task<bool?> QueryPlayStoreEnabledAsync(CancellationToken cancellationToken)
    {
        GooglePackageState? state = await QueryGooglePackageStateAsync(cancellationToken)
            .ConfigureAwait(true);
        return state?.IsPlayStoreInstalled == true
            ? !state.IsPlayStoreDisabled
            : null;
    }

    private void SetToolStatus(string resourceKey, params object[] arguments)
    {
        _toolStatusResourceKey = resourceKey;
        _toolStatusArguments = arguments?.ToArray() ?? [];
        ResolveToolStatusText();
    }

    private void RefreshToolStatusTextForLanguageChange()
    {
        ResolveToolStatusText();
    }

    private void RefreshToolActionTextForLanguageChange()
    {
        OnPropertyChanged(nameof(WifiActionText));
        OnPropertyChanged(nameof(GmsActionText));
        OnPropertyChanged(nameof(PlayStoreActionText));
    }

    private string GetNextStateText(
        bool? currentState,
        string enabledStateKey,
        string disabledStateKey,
        string unknownStateKey)
    {
        return _localization.GetString(
            currentState switch
            {
                true => enabledStateKey,
                false => disabledStateKey,
                _ => unknownStateKey
            });
    }

    private void ClearToolStatus()
    {
        _toolStatusResourceKey = null;
        _toolStatusArguments = [];
        ToolStatusText = string.Empty;
    }

    private void ResolveToolStatusText()
    {
        if (string.IsNullOrWhiteSpace(_toolStatusResourceKey))
        {
            ToolStatusText = string.Empty;
            return;
        }

        string message = _localization.GetString(_toolStatusResourceKey);
        if (_toolStatusArguments.Length > 0)
        {
            try
            {
                message = string.Format(
                    CultureInfo.CurrentCulture,
                    message,
                    _toolStatusArguments);
            }
            catch (FormatException)
            {
            }
        }

        ToolStatusText = message;
    }

}
