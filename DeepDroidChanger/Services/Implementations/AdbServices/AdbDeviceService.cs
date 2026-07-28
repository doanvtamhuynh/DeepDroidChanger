using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
namespace DeepDroidChanger.Services
{
    public sealed class AdbDeviceService : IAdbDeviceService
    {
        private const string AdbDevicesArguments = "devices";
        private const string DeviceListHeader = "List of devices attached";
        private const string OnlineStatusToken = "device";
        private const string OfflineStatusToken = "offline";
        private const string UnauthorizedStatusToken = "unauthorized";
        private static readonly char[] DeviceLineSeparators = { ' ', '\t' };

        private readonly IAdbCommandService _adbCommandService;

        public AdbDeviceService(IAdbCommandService adbCommandService)
        {
            _adbCommandService = adbCommandService;
        }

        public async Task<IReadOnlyList<AdbDevice>> GetConnectedDevicesAsync(CancellationToken cancellationToken)
        {
            var result = await _adbCommandService.RunAdbAsync(AdbDevicesArguments, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(CreateAdbFailureMessage(result));

            return ParseDevices(result.StandardOutput);
        }

        public Task<string> GetDeviceTypeAsync(string serial, CancellationToken cancellationToken)
        {
            return _adbCommandService.GetPropertyAsync(serial, PropertyConstants.DeepDroidDevice, cancellationToken);
        }

        public static IReadOnlyList<AdbDevice> ParseDevices(string output)
        {
            var devices = new List<AdbDevice>();
            var isDeviceSection = false;
            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (!isDeviceSection)
                {
                    isDeviceSection = line.Equals(DeviceListHeader, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                var parts = line.Split(DeviceLineSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                var status = ToDeviceStatus(parts[1]);
                if (status == null)
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
