using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public sealed class AdbDeviceService : IAdbDeviceService
    {
        private const string DevicesArguments = "devices";
        private const string DeviceListHeader = "List of devices attached";
        private const string OnlineStatusToken = "device";
        private const string OfflineStatusToken = "offline";
        private const string UnauthorizedStatusToken = "unauthorized";
        private static readonly char[] DeviceLineSeparators = { ' ', '\t' };
        private static readonly TimeSpan FastbootEnumerationTimeout = TimeSpan.FromSeconds(5);

        private readonly IAdbCommandService _adbCommandService;

        public AdbDeviceService(IAdbCommandService adbCommandService)
        {
            _adbCommandService = adbCommandService;
        }

        public async Task<IReadOnlyList<AdbDevice>> GetConnectedDevicesAsync(CancellationToken cancellationToken)
        {
            var adbTask = _adbCommandService.RunAdbAsync(DevicesArguments, cancellationToken);
            var fastbootTask = GetFastbootDevicesAsync(cancellationToken);
            await Task.WhenAll(adbTask, fastbootTask).ConfigureAwait(false);
            var result = await adbTask.ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(CreateAdbFailureMessage(result));

            // Fastboot is a separate USB transport; ADB cannot enumerate it.
            // Prefer its positive detection over a stale ADB row during reboot.
            return (await fastbootTask.ConfigureAwait(false))
                .Concat(ParseDevices(result.StandardOutput))
                .DistinctBy(device => device.Serial, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<IReadOnlyList<AdbDevice>> GetFastbootDevicesAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(FastbootEnumerationTimeout);
            try
            {
                var result = await _adbCommandService.RunFastbootAsync(DevicesArguments, timeout.Token).ConfigureAwait(false);
                return result.ExitCode == 0 ? ParseFastbootDevices(result.StandardOutput) : [];
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // A missing driver/tool or a timed-out USB query must not hide
                // valid ADB devices. The next refresh retries enumeration.
                return [];
            }
        }

        internal static IReadOnlyList<AdbDevice> ParseFastbootDevices(string output)
        {
            return ParseDeviceList(output, AdbDeviceStatus.Bootloader);
        }

        public async Task<bool> IsDeviceOnlineAsync(string serial, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serial);

            CommandResult result = await _adbCommandService
                .RunAdbAsync(serial, "get-state", cancellationToken)
                .ConfigureAwait(false);
            return result.ExitCode == 0
                   && string.Equals(
                       result.StandardOutput?.Trim(),
                       "device",
                       StringComparison.OrdinalIgnoreCase);
        }

        public Task<string> GetDeviceTypeAsync(string serial, CancellationToken cancellationToken)
        {
            return _adbCommandService.GetPropertyAsync(serial, PropertyConstants.DeepDroidDevice, cancellationToken);
        }

        public static IReadOnlyList<AdbDevice> ParseDevices(string output)
        {
            return ParseDeviceList(output);
        }

        public static IReadOnlyList<AdbDevice> ParseTrackedDevices(string payload)
        {
            return ParseDevices(payload);
        }

        private static IReadOnlyList<AdbDevice> ParseDeviceList(string output, AdbDeviceStatus? requiredStatus = null)
        {
            var devices = new List<AdbDevice>();
            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (line.Equals(DeviceListHeader, StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = line.Split(DeviceLineSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                var status = ToDeviceStatus(parts[1]);
                if (status is null || requiredStatus is not null && status != requiredStatus)
                    continue;

                devices.Add(new AdbDevice(parts[0], status.Value));
            }

            return devices;
        }

        private static AdbDeviceStatus? ToDeviceStatus(string status)
        {
            return status switch
            {
                OnlineStatusToken => AdbDeviceStatus.Online,
                OfflineStatusToken => AdbDeviceStatus.Offline,
                UnauthorizedStatusToken => AdbDeviceStatus.Unauthorized,
                "recovery" => AdbDeviceStatus.Recovery,
                "bootloader" or "fastboot" => AdbDeviceStatus.Bootloader,
                _ => null
            };
        }

        private static string CreateAdbFailureMessage(CommandResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                return result.StandardError.Trim();

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                return result.StandardOutput.Trim();

            return $"adb exited with code {result.ExitCode}.";
        }
    }
}
