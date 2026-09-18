
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceActionService : IDeviceActionService
    {
        private const string GmsPackageName = "com.google.android.gms";
        private const string PlayStorePackageName = "com.android.vending";

        private readonly IAdbCommandService _commandService;
        private readonly IDevicePackageService _packageService;

        public DeviceActionService(
            IAdbCommandService commandService,
            IDevicePackageService packageService)
        {
            _commandService = commandService;
            _packageService = packageService;
        }

        public Task RebootAsync(string serial, CancellationToken cancellationToken)
        {
            return _commandService.RebootAsync(serial, cancellationToken);
        }

        public async Task<GooglePackageState> GetGooglePackageStateAsync(
            string serial,
            CancellationToken cancellationToken)
        {
            Task<IReadOnlyList<string>> installedTask = _packageService
                .GetInstalledPackagesAsync(serial, cancellationToken);
            Task<IReadOnlyList<string>> disabledTask = _packageService
                .GetDisabledPackagesAsync(serial, cancellationToken);
            await Task.WhenAll(installedTask, disabledTask).ConfigureAwait(false);

            IReadOnlyList<string> installedPackages = installedTask.Result ?? [];
            IReadOnlyList<string> disabledPackages = disabledTask.Result ?? [];
            bool gmsInstalled = installedPackages.Contains(GmsPackageName, StringComparer.Ordinal);
            bool playStoreInstalled = installedPackages.Contains(PlayStorePackageName, StringComparer.Ordinal);

            return new GooglePackageState(
                IsGmsDisabled: gmsInstalled && disabledPackages.Contains(GmsPackageName, StringComparer.Ordinal),
                IsPlayStoreDisabled: playStoreInstalled && disabledPackages.Contains(PlayStorePackageName, StringComparer.Ordinal),
                IsGmsInstalled: gmsInstalled,
                IsPlayStoreInstalled: playStoreInstalled);
        }

        public Task SetGmsEnabledAsync(
            string serial,
            bool enabled,
            CancellationToken cancellationToken)
        {
            return _packageService.SetPackageEnabledAsync(
                serial,
                GmsPackageName,
                enabled,
                cancellationToken);
        }

        public Task SetPlayStoreEnabledAsync(
            string serial,
            bool enabled,
            CancellationToken cancellationToken)
        {
            return _packageService.SetPackageEnabledAsync(
                serial,
                PlayStorePackageName,
                enabled,
                cancellationToken);
        }

        public async Task<bool> GetWifiEnabledAsync(
            string serial,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serial);
            string value = await _commandService
                .GetSettingAsync(serial, "global", "wifi_on", cancellationToken)
                .ConfigureAwait(false);
            string normalized = value.Trim();
            if (normalized == "1")
                return true;
            if (normalized == "0")
                return false;

            throw new InvalidOperationException(
                $"Unable to determine Wi-Fi state for device {serial}.");
        }

        public async Task<bool?> GetScreenOnAsync(
            string serial,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serial);
            CommandResult result = await _commandService
                .RunAdbShellAsync(serial, "dumpsys power", cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
                return null;

            return ParseScreenOnState(
                $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
        }

        internal static bool? ParseScreenOnState(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            string? interactive = NormalizeValueToken(FindPropertyValue(output, "mInteractive"));
            if (bool.TryParse(interactive, out bool interactiveState))
                return interactiveState;

            string? displayState = NormalizeValueToken(FindPropertyValue(output, "mDisplayState"));
            if (IsOn(displayState))
                return true;
            if (IsOff(displayState))
                return false;

            foreach (string line in output.Split(
                         new[] { '\r', '\n' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.IndexOf("display", StringComparison.OrdinalIgnoreCase) < 0 ||
                    line.IndexOf("state", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string? state = NormalizeValueToken(FindPropertyValue(line, "state"));
                if (IsOn(state))
                    return true;
                if (IsOff(state))
                    return false;
            }

            string? wakefulness = NormalizeValueToken(FindPropertyValue(output, "mWakefulness"));
            if (string.Equals(wakefulness, "Awake", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(wakefulness, "Dreaming", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(wakefulness, "Dozing", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(wakefulness, "Asleep", StringComparison.OrdinalIgnoreCase))
                return false;

            return null;
        }

        private static string? NormalizeValueToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim();
            int tokenEnd = normalized.IndexOfAny([' ', '\t', ',', ';']);
            return (tokenEnd < 0 ? normalized : normalized[..tokenEnd]).Trim();
        }

        private static string? FindPropertyValue(string output, string key)
        {
            foreach (string line in output.Split(
                         new[] { '\r', '\n' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                int keyIndex = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                    continue;

                int separatorIndex = keyIndex + key.Length;
                while (separatorIndex < line.Length &&
                       char.IsWhiteSpace(line[separatorIndex]))
                    separatorIndex++;
                if (separatorIndex >= line.Length ||
                    (line[separatorIndex] != '=' && line[separatorIndex] != ':'))
                    continue;

                separatorIndex++;
                while (separatorIndex < line.Length &&
                       char.IsWhiteSpace(line[separatorIndex]))
                    separatorIndex++;
                return line[separatorIndex..].Trim();
            }

            return null;
        }

        private static bool IsOn(string? value) =>
            string.Equals(NormalizeValueToken(value), "ON", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeValueToken(value), "ON_UNKNOWN", StringComparison.OrdinalIgnoreCase);

        private static bool IsOff(string? value) =>
            string.Equals(NormalizeValueToken(value), "OFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeValueToken(value), "OFF_UNKNOWN", StringComparison.OrdinalIgnoreCase);

        public Task SetWifiEnabledAsync(
            string serial,
            bool enabled,
            CancellationToken cancellationToken)
        {
            return _commandService.SetWifiAsync(serial, enabled, cancellationToken);
        }
    }
}
