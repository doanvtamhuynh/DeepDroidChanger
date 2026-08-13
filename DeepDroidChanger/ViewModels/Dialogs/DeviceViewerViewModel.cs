using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
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
        private readonly ILogger<DeviceViewerViewModel> _logger;

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

        public DeviceViewerViewModel(
            IAdbCommandService adbCommandService,
            IIpGeolocationService adbIpGeolocationService,
            ILocalizationService localizationService,
            ILogger<DeviceViewerViewModel> logger)
        {
            _adbCommandService = adbCommandService;
            _adbIpGeolocationService = adbIpGeolocationService;
            _localizationService = localizationService;
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
            Serial = serial;
            DeviceName = name;
            WindowTitle = string.Format(
                _localizationService.GetString("DeviceViewer_WindowTitleFormat"),
                name,
                serial);
            DeviceIpText = GetLogText("Log_DeviceViewerIpChecking");
            MarkStarting();
        }

        public void MarkStarting()
        {
            IsStreamLoading = true;
            HasStreamError = false;
            IsWaitingForDevice = false;

            IsDeviceConnected = false;
            IsDeviceDisconnected = false;
            IsDeviceChecking = true;

            DeviceStatusText = _localizationService.GetString("DeviceViewer_StatusChecking");
            StreamLogText = GetLogText("Log_StartingStream");
        }

        public void MarkStreaming()
        {
            HasStreamError = false;
            IsWaitingForDevice = false;
            IsStreamLoading = false;

            IsDeviceConnected = true;
            IsDeviceDisconnected = false;
            IsDeviceChecking = false;

            DeviceStatusText = _localizationService.GetString("DeviceViewer_StatusConnected");
            StreamLogText = GetLogText("Log_Streaming");
        }

        public void MarkStreamError()
        {
            HasStreamError = true;
            IsWaitingForDevice = false;
            IsStreamLoading = false;

            IsDeviceConnected = false;
            IsDeviceDisconnected = true;
            IsDeviceChecking = false;

            DeviceStatusText = _localizationService.GetString("DeviceViewer_StatusDisconnected");
            StreamLogText = GetLogText("Log_StreamFailed");
        }

        public void MarkWaitingForDevice()
        {
            IsWaitingForDevice = true;
            HasStreamError = false;
            IsStreamLoading = false;

            IsDeviceConnected = false;
            IsDeviceDisconnected = true;
            IsDeviceChecking = false;

            DeviceStatusText = _localizationService.GetString("DeviceViewer_StatusDisconnected");
            StreamLogText = GetLogText("Log_WaitingForDevice");
        }

        public void MarkDeviceIpUnavailable()
        {
            DeviceIpText = GetLogText("Log_DeviceViewerNoInternet");
        }

        public async Task RefreshDeviceIpAsync(CancellationToken cancellationToken, bool showCheckingState)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (showCheckingState)
                DeviceIpText = GetLogText("Log_DeviceViewerIpChecking");

            if (string.IsNullOrWhiteSpace(Serial))
            {
                MarkDeviceIpUnavailable();
                return;
            }

            try
            {
                var info = await _adbIpGeolocationService.GetDeviceIpGeolocationAsync(Serial, cancellationToken).ConfigureAwait(true);
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
                _logger.LogDebug(exception, "Device viewer IP lookup failed.");
                _logger.LogWarning("Device viewer IP lookup failed; showing no internet.");
                MarkDeviceIpUnavailable();
            }
        }

        private string GetLogText(string resourceKey)
        {
            return _localizationService.GetString(resourceKey);
        }

        private void SetDeviceLog(string resourceKey)
        {
            StreamLogText = GetLogText(resourceKey);
        }

        private void SetFormattedDeviceLog(string resourceKey, params object[] args)
        {
            StreamLogText = string.Format(GetLogText(resourceKey), args);
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

        private async Task ExecuteInputOperationAsync(Func<Task> action, string actionResourceKey)
        {
            if (!TryValidateSerial(actionResourceKey))
                return;

            string actionName = GetLogText(actionResourceKey);

            try
            {
                await action();
                SetFormattedDeviceLog("Log_SendKeyEventSuccessFormat", actionName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform {ActionName} on device {Serial}.", actionName, Serial);
                SetFormattedDeviceLog("Log_SendKeyEventFailedFormat", actionName);
            }
        }

        [RelayCommand]
        private Task BackAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(() => _adbCommandService.SendKeyEventAsync(Serial, 4, cancellationToken), "Log_ActionBack");

        [RelayCommand]
        private Task HomeAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(() => _adbCommandService.SendKeyEventAsync(Serial, 3, cancellationToken), "Log_ActionHome");

        [RelayCommand]
        private Task RecentAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(() => _adbCommandService.SendKeyEventAsync(Serial, 187, cancellationToken), "Log_ActionRecent");

        [RelayCommand]
        private Task PowerAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(() => _adbCommandService.SendKeyEventAsync(Serial, 26, cancellationToken), "Log_ActionPower");

        [RelayCommand]
        private Task VolumeUpAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(() => _adbCommandService.SendKeyEventAsync(Serial, 24, cancellationToken), "Log_ActionVolumeUp");

        [RelayCommand]
        private Task VolumeDownAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(() => _adbCommandService.SendKeyEventAsync(Serial, 25, cancellationToken), "Log_ActionVolumeDown");

        [RelayCommand]
        private Task EnterAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(() => _adbCommandService.SendKeyEventAsync(Serial, 66, cancellationToken), "Log_ActionEnter");

        [RelayCommand]
        private Task ScreenshotAsync(CancellationToken cancellationToken) =>
            ExecuteInputOperationAsync(() => _adbCommandService.SendKeyEventAsync(Serial, 120, cancellationToken), "Log_ActionScreenshot");

        [RelayCommand]
        private async Task SendInputTextAsync(CancellationToken cancellationToken)
        {
            if (!TryValidateSerial("Log_ActionInputText"))
                return;

            if (string.IsNullOrWhiteSpace(InputText))
            {
                SetDeviceLog("Log_InputTextEmpty");
                return;
            }

            try
            {
                await _adbCommandService.SendTextAsync(Serial, InputText, cancellationToken).ConfigureAwait(true);
                SetDeviceLog("Log_InputTextSent");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send input text to device {Serial}.", Serial);
                SetDeviceLog("Log_InputTextFailed");
            }
        }

        [RelayCommand]
        private async Task RunShellCommandAsync(CancellationToken cancellationToken)
        {
            if (!TryValidateSerial("Log_ActionShellCommand"))
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run ADB shell command on device {Serial}.", Serial);
                SetFormattedDeviceLog(
                    "Log_ShellCommandFailedFormat",
                    ex.Message);
            }
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
