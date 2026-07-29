
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
            IReadOnlyList<string> disabledPackages = await _packageService
                .GetDisabledPackagesAsync(serial, cancellationToken)
                .ConfigureAwait(false);

            return new GooglePackageState(
                disabledPackages.Contains(GmsPackageName, StringComparer.Ordinal),
                disabledPackages.Contains(PlayStorePackageName, StringComparer.Ordinal));
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
            string value = await _commandService
                .GetSettingAsync(serial, "global", "wifi_on", cancellationToken)
                .ConfigureAwait(false);
            return string.Equals(value.Trim(), "1", StringComparison.Ordinal);
        }

        public Task SetWifiEnabledAsync(
            string serial,
            bool enabled,
            CancellationToken cancellationToken)
        {
            return _commandService.SetWifiAsync(serial, enabled, cancellationToken);
        }
    }
}
