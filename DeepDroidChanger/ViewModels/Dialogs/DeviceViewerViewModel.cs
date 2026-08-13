using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
    public enum DeviceConnectionState
    {
        Checking,
        Online,
        Offline,
        Unauthorized
    }

    public enum DeviceViewerStreamState
    {
        Starting,
        Streaming,
        Reconnecting,
        Error,
        WaitingForDevice
    }

    public sealed partial class DeviceViewerViewModel : ObservableObject
    {
        private const int ShellOutputPreviewMaxLength = 180;
        private const string ShellOutputEllipsis = "...";
        private const char CarriageReturn = '\r';
        private const char NewLine = '\n';
        private const char Space = ' ';
        private static readonly char[] ShellOutputLineSeparators = [CarriageReturn, NewLine];

        private readonly IAdbCommandService _adbCommandService;
        private readonly IIpGeolocationService _adbIpGeolocationService;
        private readonly ILocalizationService _localizationService;
        private readonly IDeviceActionCoordinatorService _deviceActionCoordinator;
        private readonly ILogger<DeviceViewerViewModel> _logger;
        private readonly SemaphoreSlim _quickCommandGate = new(1, 1);
        private readonly CancellationTokenSource _commandLifetime = new();
        private SynchronizationContext? _uiContext;
        private DeviceConnectionState _deviceConnectionState = DeviceConnectionState.Checking;
        private DeviceViewerStreamState _streamState = DeviceViewerStreamState.Starting;
        private bool _disposed;
        private bool _coordinatorSubscribed;

        [ObservableProperty]
        private string _serial = string.Empty;

        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _windowTitle = string.Empty;

        [ObservableProperty]
        private bool _isStreamLoading = true;

        [ObservableProperty]
        private bool _hasStreamError;

        [ObservableProperty]
        private bool _isWaitingForDevice;

        [ObservableProperty]
        private bool _isActionsPanelExpanded;

        [ObservableProperty]
        private string _deviceStatusText = string.Empty;

        [ObservableProperty]
        private string _deviceIpText = string.Empty;

        [ObservableProperty]
        private bool _isDeviceConnected;

        [ObservableProperty]
        private bool _isDeviceDisconnected;

        [ObservableProperty]
        private bool _isDeviceChecking;

        [ObservableProperty]
        private string _streamLogText = string.Empty;

        [ObservableProperty]
        private string _inputText = string.Empty;

        [ObservableProperty]
        private string _shellCommand = string.Empty;

        public bool IsStreaming => !IsStreamLoading && !HasStreamError && !IsWaitingForDevice;

        public DeviceConnectionState DeviceConnectionState => _deviceConnectionState;

        public DeviceViewerStreamState StreamState => _streamState;

        public DeviceViewerViewModel(
            IAdbCommandService adbCommandService,
            IIpGeolocationService adbIpGeolocationService,
            ILocalizationService localizationService,
            IDeviceActionCoordinatorService deviceActionCoordinator,
            ILogger<DeviceViewerViewModel> logger)
        {
            _adbCommandService = adbCommandService;
            _adbIpGeolocationService = adbIpGeolocationService;
            _localizationService = localizationService;
            _deviceActionCoordinator = deviceActionCoordinator;
            _logger = logger;
        }

        [RelayCommand]
        private void ToggleActionsPanel()
        {
            IsActionsPanelExpanded = !IsActionsPanelExpanded;
        }

        partial void OnIsStreamLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsStreaming));
        }

        partial void OnHasStreamErrorChanged(bool value)
        {
            OnPropertyChanged(nameof(IsStreaming));
        }

        partial void OnIsWaitingForDeviceChanged(bool value)
        {
            OnPropertyChanged(nameof(IsStreaming));
        }

        [ObservableProperty]
        private double _deviceAspectRatio = 9.0 / 20.0;

        public void Initialize(string serial, string name)
        {
            if (_disposed)
                return;

            _uiContext = SynchronizationContext.Current;
            if (!_coordinatorSubscribed)
            {
                _deviceActionCoordinator.OperationStateChanged += OnOperationStateChanged;
                _coordinatorSubscribed = true;
            }

            Serial = serial;
            DeviceName = name;
            WindowTitle = string.Format(
                _localizationService.GetString("DeviceViewer_WindowTitleFormat"),
                name,
                serial);
            DeviceIpText = GetLogText("Log_DeviceViewerIpChecking");
            SetDeviceConnectionState(DeviceConnectionState.Checking);
            MarkStarting();
        }

        public void MarkStarting()
        {
            if (_disposed)
                return;

            SetStreamState(DeviceViewerStreamState.Starting);
            IsStreamLoading = true;
            HasStreamError = false;
            IsWaitingForDevice = false;
            StreamLogText = GetLogText("Log_StartingStream");
        }

        public void MarkStreaming()
        {
            if (_disposed)
                return;

            SetStreamState(DeviceViewerStreamState.Streaming);
            HasStreamError = false;
            IsWaitingForDevice = false;
            IsStreamLoading = false;

            StreamLogText = GetLogText("Log_Streaming");
        }

        public void MarkStreamError()
        {
            if (_disposed)
                return;

            SetStreamState(DeviceViewerStreamState.Error);
            HasStreamError = true;
            IsWaitingForDevice = false;
            IsStreamLoading = false;

            StreamLogText = GetLogText("Log_StreamFailed");
        }

        public void MarkReconnecting()
        {
            if (_disposed)
                return;

            SetStreamState(DeviceViewerStreamState.Reconnecting);
            IsStreamLoading = true;
            HasStreamError = false;
            IsWaitingForDevice = false;
            StreamLogText = GetLogText("Log_StreamFailed");
        }

        public void MarkWaitingForDevice()
        {
            if (_disposed)
                return;

            SetStreamState(DeviceViewerStreamState.WaitingForDevice);
            IsWaitingForDevice = true;
            HasStreamError = false;
            IsStreamLoading = false;

            StreamLogText = GetLogText("Log_WaitingForDevice");
        }

        public void SetDeviceConnectionState(DeviceConnectionState state)
        {
            if (_disposed)
                return;

            var changed = _deviceConnectionState != state;
            _deviceConnectionState = state;
            IsDeviceConnected = state == DeviceConnectionState.Online;
            IsDeviceDisconnected = state is DeviceConnectionState.Offline or DeviceConnectionState.Unauthorized;
            IsDeviceChecking = state == DeviceConnectionState.Checking;
            DeviceStatusText = state switch
            {
                DeviceConnectionState.Online => _localizationService.GetString("DeviceViewer_StatusConnected"),
                DeviceConnectionState.Checking => _localizationService.GetString("DeviceViewer_StatusChecking"),
                _ => _localizationService.GetString("DeviceViewer_StatusDisconnected")
            };
            if (changed)
                OnPropertyChanged(nameof(DeviceConnectionState));
            NotifyCommandsCanExecuteChanged();
        }

        public void MarkDeviceIpUnavailable()
        {
            if (_disposed)
                return;

            DeviceIpText = GetLogText("Log_DeviceViewerNoInternet");
        }

        public async Task RefreshDeviceIpAsync(
            CancellationToken cancellationToken,
            bool showCheckingState,
            Func<bool>? canApplyResult = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_disposed)
                return;

            if (!CanApplyDeviceIpResult(canApplyResult))
                return;

            if (showCheckingState)
                DeviceIpText = GetLogText("Log_DeviceViewerIpChecking");

            if (string.IsNullOrWhiteSpace(Serial))
            {
                if (!CanApplyDeviceIpResult(canApplyResult))
                    return;

                MarkDeviceIpUnavailable();
                return;
            }

            try
            {
                var info = await _adbIpGeolocationService.GetDeviceIpGeolocationAsync(Serial, cancellationToken).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanApplyDeviceIpResult(canApplyResult))
                    return;

                DeviceIpText = string.IsNullOrWhiteSpace(info.PublicIp)
                    ? GetLogText("Log_DeviceViewerNoInternet")
                    : info.PublicIp;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException exception)
            {
                if (!CanApplyDeviceIpResult(canApplyResult))
                    return;

                _logger.LogDebug(exception, "Device viewer IP lookup failed.");
                _logger.LogWarning("Device viewer IP lookup failed; showing no internet.");
                MarkDeviceIpUnavailable();
            }
        }

        private static bool CanApplyDeviceIpResult(Func<bool>? canApplyResult)
        {
            return canApplyResult?.Invoke() != false;
        }

        private string GetLogText(string resourceKey)
        {
            return _localizationService.GetString(resourceKey);
        }

        private void SetDeviceLog(string resourceKey)
        {
            if (_disposed)
                return;

            StreamLogText = GetLogText(resourceKey);
        }

        private void SetFormattedDeviceLog(string resourceKey, params object[] args)
        {
            if (_disposed)
                return;

            StreamLogText = string.Format(GetLogText(resourceKey), args);
        }

        private void SetStreamState(DeviceViewerStreamState state)
        {
            if (_streamState == state)
                return;

            _streamState = state;
            OnPropertyChanged(nameof(StreamState));
        }

        private bool TryValidateSerial(string actionResourceKey)
        {
            if (!string.IsNullOrEmpty(Serial))
                return true;

            _logger.LogWarning(
                "Execution of {ActionName} skipped because device serial is empty.",
                GetLogText(actionResourceKey));
            SetDeviceLog("Log_MissingSerial");
            return false;
        }

        private bool CanExecuteDeviceCommand()
        {
            return !_disposed && DeviceConnectionState == DeviceConnectionState.Online;
        }

        private bool CanExecuteSensitiveDeviceCommand()
        {
            return CanExecuteDeviceCommand() && !_deviceActionCoordinator.IsBusy(Serial);
        }

        private async Task ExecuteInputOperationAsync(
            Func<CancellationToken, Task> action,
            string actionResourceKey,
            CancellationToken cancellationToken)
        {
            if (!CanExecuteDeviceCommand() || !TryValidateSerial(actionResourceKey))
                return;

            string actionName = GetLogText(actionResourceKey);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _commandLifetime.Token);

            try
            {
                await _quickCommandGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(true);
                try
                {
                    await action(linkedCancellation.Token);
                }
                finally
                {
                    _quickCommandGate.Release();
                }

                if (_disposed)
                    return;

                SetFormattedDeviceLog("Log_SendKeyEventSuccessFormat", actionName);
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
                _logger.LogDebug("Device viewer command {ActionName} was canceled for {Serial}.", actionName, Serial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform {ActionName} on device {Serial}.", actionName, Serial);
                SetFormattedDeviceLog("Log_SendKeyEventFailedFormat", actionName);
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceCommand))]
        private Task BackAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(token => _adbCommandService.SendKeyEventAsync(Serial, 4, token), "Log_ActionBack", cancellationToken);

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceCommand))]
        private Task HomeAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(token => _adbCommandService.SendKeyEventAsync(Serial, 3, token), "Log_ActionHome", cancellationToken);

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceCommand))]
        private Task RecentAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(token => _adbCommandService.SendKeyEventAsync(Serial, 187, token), "Log_ActionRecent", cancellationToken);

        [RelayCommand(CanExecute = nameof(CanExecuteSensitiveDeviceCommand))]
        private Task PowerAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(token => _adbCommandService.SendKeyEventAsync(Serial, 26, token), "Log_ActionPower", cancellationToken);

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceCommand))]
        private Task VolumeUpAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(token => _adbCommandService.SendKeyEventAsync(Serial, 24, token), "Log_ActionVolumeUp", cancellationToken);

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceCommand))]
        private Task VolumeDownAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(token => _adbCommandService.SendKeyEventAsync(Serial, 25, token), "Log_ActionVolumeDown", cancellationToken);

        [RelayCommand(CanExecute = nameof(CanExecuteSensitiveDeviceCommand))]
        private Task EnterAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(token => _adbCommandService.SendKeyEventAsync(Serial, 66, token), "Log_ActionEnter", cancellationToken);

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceCommand))]
        private Task ScreenshotAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(token => _adbCommandService.SendKeyEventAsync(Serial, 120, token), "Log_ActionScreenshot", cancellationToken);

        [RelayCommand(CanExecute = nameof(CanExecuteSensitiveDeviceCommand))]
        private async Task SendInputTextAsync(CancellationToken cancellationToken)
        {
            if (!CanExecuteSensitiveDeviceCommand() || !TryValidateSerial("Log_ActionInputText"))
                return;

            if (string.IsNullOrWhiteSpace(InputText))
            {
                SetDeviceLog("Log_InputTextEmpty");
                return;
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _commandLifetime.Token);

            try
            {
                await _quickCommandGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(true);
                try
                {
                    await _adbCommandService.SendTextAsync(Serial, InputText, linkedCancellation.Token).ConfigureAwait(true);
                }
                finally
                {
                    _quickCommandGate.Release();
                }

                if (_disposed)
                    return;

                SetDeviceLog("Log_InputTextSent");
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
                _logger.LogDebug("Device viewer text input was canceled for {Serial}.", Serial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send input text to device {Serial}.", Serial);
                SetDeviceLog("Log_InputTextFailed");
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSensitiveDeviceCommand))]
        private async Task RunShellCommandAsync(CancellationToken cancellationToken)
        {
            if (!CanExecuteSensitiveDeviceCommand() || !TryValidateSerial("Log_ActionShellCommand"))
                return;

            var command = ShellCommand.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                SetDeviceLog("Log_ShellCommandEmpty");
                return;
            }

            try
            {
                var result = await _adbCommandService.RunAdbShellAsync(Serial, command, cancellationToken).ConfigureAwait(true);
                var summary = GetCommandResultSummary(result);
                if (result.ExitCode == 0)
                {
                    if (string.IsNullOrEmpty(summary))
                    {
                        SetDeviceLog("Log_ShellCommandNoOutput");
                        return;
                    }

                    SetFormattedDeviceLog(
                        "Log_ShellCommandSuccessFormat",
                        summary);
                    return;
                }

                _logger.LogWarning(
                    "ADB shell command failed on device {Serial}. ExitCode: {ExitCode}. OutputLength: {OutputLength}. ErrorLength: {ErrorLength}",
                    Serial,
                    result.ExitCode,
                    result.StandardOutput.Length,
                    result.StandardError.Length);

                SetFormattedDeviceLog(
                    "Log_ShellCommandFailedFormat",
                    string.IsNullOrEmpty(summary)
                        ? GetLogText("Log_ShellCommandUnknownResult")
                        : summary);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _commandLifetime.IsCancellationRequested)
            {
                _logger.LogDebug("Device viewer shell command was canceled for {Serial}.", Serial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run ADB shell command on device {Serial}.", Serial);
                SetFormattedDeviceLog(
                    "Log_ShellCommandFailedFormat",
                    ex.Message);
            }
        }

        private void OnOperationStateChanged(DeviceActionOperationSnapshot snapshot)
        {
            if (!string.Equals(snapshot.Serial, Serial, StringComparison.OrdinalIgnoreCase))
                return;

            void Notify() => NotifyCommandsCanExecuteChanged();
            if (_uiContext is { } context && SynchronizationContext.Current != context)
                context.Post(_ => Notify(), null);
            else
                Notify();
        }

        private void NotifyCommandsCanExecuteChanged()
        {
            if (_disposed)
                return;

            BackCommand.NotifyCanExecuteChanged();
            HomeCommand.NotifyCanExecuteChanged();
            RecentCommand.NotifyCanExecuteChanged();
            PowerCommand.NotifyCanExecuteChanged();
            VolumeUpCommand.NotifyCanExecuteChanged();
            VolumeDownCommand.NotifyCanExecuteChanged();
            EnterCommand.NotifyCanExecuteChanged();
            ScreenshotCommand.NotifyCanExecuteChanged();
            SendInputTextCommand.NotifyCanExecuteChanged();
            RunShellCommandCommand.NotifyCanExecuteChanged();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_coordinatorSubscribed)
            {
                _deviceActionCoordinator.OperationStateChanged -= OnOperationStateChanged;
                _coordinatorSubscribed = false;
            }

            try
            {
                _commandLifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _commandLifetime.Dispose();
        }

        private string GetCommandResultSummary(CommandResult result)
        {
            var output = ToSingleLineSummary(result.StandardOutput);
            if (!string.IsNullOrEmpty(output))
                return output;

            return ToSingleLineSummary(result.StandardError);
        }

        private static string ToSingleLineSummary(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var summary = string.Join(
                Space,
                value.Split(ShellOutputLineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            return summary.Length <= ShellOutputPreviewMaxLength
                ? summary
                : summary[..ShellOutputPreviewMaxLength] + ShellOutputEllipsis;
        }
    }
}
