using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class AdbCommandService : IAdbCommandService
    {
        private const char CarriageReturn = '\r';
        private const char NewLine = '\n';
        private const char Tab = '\t';
        private const char Space = ' ';
        private const string WindowsNewLine = "\r\n";
        private const string SpaceText = " ";
        private const string SendInputTextPurpose = "send input text";

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _readOnlyPropertyLocks =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly IProcessRunnerService _commandRunner;
        private readonly ILogger<AdbCommandService> _logger;
        private readonly AdbToolPathResolver _toolPathResolver;

        public AdbCommandService(
            IProcessRunnerService commandRunner,
            ILogger<AdbCommandService> logger,
            AdbToolPathResolver? toolPathResolver = null)
        {
            _commandRunner = commandRunner;
            _logger = logger;
            _toolPathResolver = toolPathResolver ?? new AdbToolPathResolver();
        }

        public async Task<CommandResult> RunAdbAsync(string arguments, CancellationToken cancellationToken)
        {
            var adbPath = _toolPathResolver.GetAdbPath();
            _logger.LogDebug(
                "Running ADB tool {AdbPath}. ArgumentLength: {ArgumentLength}",
                adbPath,
                arguments.Length);
            return await _commandRunner.RunAsync(adbPath, arguments, cancellationToken).ConfigureAwait(false);
        }

        public Task<CommandResult> RunAdbAsync(string serial, string arguments, CancellationToken cancellationToken)
        {
            return RunAdbAsync($"{"-s"} {QuoteProcessArgument(serial)} {arguments}", cancellationToken);
        }

        public Task<CommandResult> RunAdbShellAsync(string serial, string shellCommand, CancellationToken cancellationToken)
        {
            return RunAdbAsync(serial, $"shell {shellCommand}", cancellationToken);
        }

        public async Task<CommandResult> RunAdbShellScriptAsync(
            string serial,
            string shellScript,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serial);
            ArgumentException.ThrowIfNullOrWhiteSpace(shellScript);

            string adbPath = _toolPathResolver.GetAdbPath();
            string arguments = $"{"-s"} {QuoteProcessArgument(serial)} shell sh";
            string normalizedScript = shellScript
                .Replace(WindowsNewLine, "\n", StringComparison.Ordinal)
                .Replace(CarriageReturn, NewLine);
            if (!normalizedScript.EndsWith(NewLine))
                normalizedScript += NewLine;

            _logger.LogDebug(
                "Running ADB shell script on {Serial}. ScriptLength: {ScriptLength}",
                serial,
                normalizedScript.Length);
            return await _commandRunner
                .RunAsync(adbPath, arguments, normalizedScript, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<CommandResult> RunFastbootAsync(string arguments, CancellationToken cancellationToken)
        {
            var fastbootPath = _toolPathResolver.GetFastbootPath();
            _logger.LogDebug(
                "Running Fastboot tool {FastbootPath}. ArgumentLength: {ArgumentLength}",
                fastbootPath,
                arguments.Length);
            return await _commandRunner.RunAsync(fastbootPath, arguments, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> GetPropertyAsync(string serial, string propertyName, CancellationToken cancellationToken)
        {
            var command = $"getprop {propertyName}";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            return ProcessCommandResult(result, serial, $"get property {propertyName}", isWrite: false);
        }

        public async Task SetPropertyAsync(string serial, string propertyName, string value, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serial);
            ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
            ArgumentNullException.ThrowIfNull(value);

            if (!propertyName.StartsWith(PropertyConstants.ReadOnlyPrefix, StringComparison.Ordinal))
            {
                await SetPropertyCoreAsync(serial, propertyName, value, cancellationToken).ConfigureAwait(false);
                return;
            }

            SemaphoreSlim propertyLock = _readOnlyPropertyLocks.GetOrAdd(
                serial,
                static _ => new SemaphoreSlim(1, 1));
            await propertyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SetReadOnlyPropertyAsync(serial, propertyName, value, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                propertyLock.Release();
            }
        }

        private async Task SetReadOnlyPropertyAsync(
            string serial,
            string propertyName,
            string value,
            CancellationToken cancellationToken)
        {
            Exception? primaryException = null;
            try
            {
                await SetPropertyCoreAsync(
                        serial,
                        PropertyConstants.Spoof.BypassReadOnlyProperties,
                        "1",
                        cancellationToken)
                    .ConfigureAwait(false);
                await SetPropertyCoreAsync(serial, propertyName, value, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                primaryException = exception;
                throw;
            }
            finally
            {
                try
                {
                    await SetPropertyCoreAsync(
                            serial,
                            PropertyConstants.Spoof.BypassReadOnlyProperties,
                            "0",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (primaryException is not null)
                {
                    _logger.LogError(
                        exception,
                        "Failed to disable read-only property bypass on device {Serial} after setting {PropertyName} failed.",
                        serial,
                        propertyName);
                }
            }
        }

        private async Task SetPropertyCoreAsync(
            string serial,
            string propertyName,
            string value,
            CancellationToken cancellationToken)
        {
            string command = $"setprop {propertyName} {QuoteShellValue(value)}";
            CommandResult result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, $"set property {propertyName}", isWrite: true);
        }

        public async Task<string> GetSettingAsync(string serial, string namespaceName, string key, CancellationToken cancellationToken)
        {
            var command = $"settings get {namespaceName} {key}";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            return ProcessCommandResult(result, serial, $"get setting {namespaceName}.{key}", isWrite: false);
        }

        public async Task PutSettingAsync(string serial, string namespaceName, string key, string value, CancellationToken cancellationToken)
        {
            var command = $"settings put {namespaceName} {key} {QuoteShellValue(value)}";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, $"put setting {namespaceName}.{key}", isWrite: true);
        }

        public async Task DeleteSettingAsync(string serial, string namespaceName, string key, CancellationToken cancellationToken)
        {
            var command = $"settings delete {namespaceName} {key}";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, $"delete setting {namespaceName}.{key}", isWrite: true);
        }

        public async Task BroadcastAsync(string serial, string action, CancellationToken cancellationToken)
        {
            var command = $"am broadcast -a {action}";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, $"broadcast intent {action}", isWrite: true);
        }

        public async Task<string> CurlAsync(string serial, string url, CancellationToken cancellationToken)
        {
            var command = $"curl --fail --silent --show-error --max-time {15} {QuoteShellValue(url)}";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            return ProcessCommandResult(result, serial, "curl endpoint", isWrite: false);
        }

        private string ProcessCommandResult(CommandResult result, string serial, string purpose, bool isWrite)
        {
            _logger.LogDebug(
                "Adb command for {Serial} ({Purpose}) completed. ExitCode: {ExitCode}. OutputLength: {OutLen}. ErrorLength: {ErrLen}",
                serial, purpose, result.ExitCode, result.StandardOutput?.Length ?? 0, result.StandardError?.Length ?? 0);

            if (result.ExitCode != 0)
            {
                _logger.LogError(
                    "Adb command failed. Serial: {Serial}, Purpose: {Purpose}, ExitCode: {ExitCode}, OutputLength: {OutputLength}, ErrorLength: {ErrorLength}",
                    serial,
                    purpose,
                    result.ExitCode,
                    result.StandardOutput?.Length ?? 0,
                    result.StandardError?.Length ?? 0);

                if (isWrite)
                {
                    throw new InvalidOperationException(
                        $"ADB write operation '{purpose}' failed on device {serial} with exit code {result.ExitCode}.");
                }

                return string.Empty;
            }

            return result.StandardOutput?.Trim() ?? string.Empty;
        }

        private static string QuoteProcessArgument(string value)
        {
            return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        private static string QuoteShellValue(string value)
        {
            return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
        }

        public async Task SendKeyEventAsync(string serial, int keyCode, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Sending keyevent {KeyCode} to device {Serial}.", keyCode, serial);
            var command = string.Format("input keyevent {0}", keyCode);
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to send keyevent {keyCode} to device {serial} with exit code {result.ExitCode}.");
            }
            _logger.LogInformation("Successfully sent keyevent {KeyCode} to device {Serial}.", keyCode, serial);
        }

        public async Task SendTextAsync(string serial, string text, CancellationToken cancellationToken)
        {
            var normalizedText = NormalizeInputText(text);
            _logger.LogInformation("Sending input text to device {Serial}. TextLength: {TextLength}.", serial, text.Length);

            var command = string.Format(
                "input text {0}",
                QuoteShellValue(normalizedText));
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, SendInputTextPurpose, isWrite: true);

            _logger.LogInformation("Successfully sent input text to device {Serial}.", serial);
        }

        public async Task RebootAsync(string serial, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Sending reboot command to device {Serial}.", serial);
            var result = await RunAdbAsync(serial, "reboot", cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to reboot device {serial} with exit code {result.ExitCode}.");
            }
            _logger.LogInformation("Successfully sent reboot command to device {Serial}.", serial);
        }

        public async Task SetWifiAsync(string serial, bool enabled, CancellationToken cancellationToken)
        {
            var state = enabled ? "enable" : "disable";
            _logger.LogInformation("{State} Wi-Fi on device {Serial}.", enabled ? "Enabling" : "Disabling", serial);
            var command = $"svc wifi {state}";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, $"{state} Wi-Fi", isWrite: true);
        }

        public async Task<bool> IsWifiEnabledAsync(string serial, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serial);

            CommandResult? statusResult = null;
            try
            {
                statusResult = await RunAdbShellAsync(
                        serial,
                        "cmd wifi status",
                        cancellationToken)
                    .ConfigureAwait(false);
                string statusText = $"{statusResult.StandardOutput}\n{statusResult.StandardError}";
                if (statusResult.ExitCode == 0)
                {
                    if (Regex.IsMatch(statusText, @"\bwi[- ]?fi\s+is\s+enabled\b", RegexOptions.IgnoreCase))
                        return true;

                    if (Regex.IsMatch(statusText, @"\bwi[- ]?fi\s+is\s+disabled\b", RegexOptions.IgnoreCase))
                        return false;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "The cmd wifi status query failed for device {Serial}.", serial);
            }

            try
            {
                CommandResult settingResult = await RunAdbShellAsync(
                        serial,
                        "settings get global wifi_on",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (settingResult.ExitCode == 0)
                {
                    string value = settingResult.StandardOutput.Trim();
                    if (value == "1")
                        return true;

                    if (value == "0")
                        return false;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "The global wifi_on query failed for device {Serial}.", serial);
            }

            _logger.LogWarning(
                "Unable to determine Wi-Fi state for device {Serial}; treating Wi-Fi as disabled.",
                serial);
            return false;
        }

        public async Task OpenPackageAsync(string serial, string packageName, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Opening package {PackageName} on device {Serial}.", packageName, serial);
            var command = $"monkey -p {packageName} -c android.intent.category.LAUNCHER 1";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, $"open package {packageName}", isWrite: true);
        }

        public async Task ForceStopPackageAsync(string serial, string packageName, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Force stopping package {PackageName} on device {Serial}.", packageName, serial);
            var command = $"am force-stop {packageName}";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, $"force stop package {packageName}", isWrite: true);
        }

        public async Task ClearPackageAsync(string serial, string packageName, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Clearing package {PackageName} on device {Serial}.", packageName, serial);
            var command = $"pm clear {packageName}";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, $"clear package {packageName}", isWrite: true);
        }

        public async Task OpenWifiSettingsAsync(string serial, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Opening Wi-Fi settings on device {Serial}.", serial);
            var command = "am start -a android.settings.WIFI_SETTINGS";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, "open Wi-Fi settings", isWrite: true);
        }

        public async Task OpenLinkAsync(string serial, string url, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Opening configured URL on device {Serial}.", serial);
            var command = $"am start -a android.intent.action.VIEW -d \"{EscapeIntentUrl(url)}\"";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, "open browser URL", isWrite: true);
        }

        public async Task ClearGlobalHttpProxyAsync(string serial, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Clearing global HTTP proxy on device {Serial}.", serial);
            var command = "settings put global http_proxy null";
            var result = await RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
            ProcessCommandResult(result, serial, "clear global HTTP proxy", isWrite: true);
        }

        private static string EscapeIntentUrl(string value)
        {
            return value.Replace("&", "\\&", StringComparison.Ordinal);
        }

        private static string NormalizeInputText(string text)
        {
            return text
                .Replace(WindowsNewLine, SpaceText, StringComparison.Ordinal)
                .Replace(CarriageReturn, Space)
                .Replace(NewLine, Space)
                .Replace(Tab, Space)
                .Replace(SpaceText, "%s", StringComparison.Ordinal);
        }
    }
}
