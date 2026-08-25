using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceConfigService : IDeviceConfigService
    {
        private readonly IDeviceStoreService _deviceStoreService;
        private readonly ISettingsService _settingsService;
        private readonly AppSettings _settings;

        public DeviceConfigService(
            IDeviceStoreService deviceStoreService,
            ISettingsService settingsService,
            AppSettings settings)
        {
            _deviceStoreService = deviceStoreService;
            _settingsService = settingsService;
            _settings = settings;
        }

        public Task SaveSettingsAsync(CancellationToken cancellationToken)
        {
            return _settingsService.SaveAsync(_settings, cancellationToken);
        }

        public async Task<bool> SaveDeviceRowAsync(
            IList<StoredDeviceConfig> storedDevices,
            string serial,
            string name,
            string type,
            CarrierCountryOption? selectedCountry,
            CarrierOption? selectedCarrier,
            bool includeSelectedCarrierConfig,
            CancellationToken cancellationToken)
        {
            var storedDevice = storedDevices.FirstOrDefault(device => DeviceRowFactory.SerialEquals(device.Serial, serial));
            if (storedDevice == null)
                return false;

            void Apply(StoredDeviceConfig device)
            {
                device.Name = name.Trim();
                device.Type = DeviceTypeHelper.Normalize(type);
                if (includeSelectedCarrierConfig)
                    ApplySelectedCarrierConfig(device, selectedCountry, selectedCarrier);
            }

            bool updated = await _deviceStoreService.UpdateAsync(serial, Apply, cancellationToken).ConfigureAwait(false);
            if (updated)
                Apply(storedDevice);

            return updated;
        }

        public async Task<bool> SaveUpdateIntegrityConfigAsync(
            IList<StoredDeviceConfig> storedDevices,
            string serial,
            UpdateIntegrityDialogResult result,
            CancellationToken cancellationToken)
        {
            var storedDevice = storedDevices.FirstOrDefault(device => DeviceRowFactory.SerialEquals(device.Serial, serial));
            if (storedDevice == null)
                return false;

            void Apply(StoredDeviceConfig device)
            {
                device.UpdateIntegrityFromServer = result.UpdateIntegrityFromServer;
                device.UpdateIntegrityEnabled = result.UpdateIntegrityEnabled;
                device.UpdateKeyboxEnabled = result.UpdateKeyboxEnabled;
                device.UpdateIntegrityFile = result.UpdateIntegrityFile;
                device.UpdateKeyboxFile = result.UpdateKeyboxFile;
            }

            bool updated = await _deviceStoreService.UpdateAsync(serial, Apply, cancellationToken).ConfigureAwait(false);
            if (updated)
                Apply(storedDevice);

            return updated;
        }

        public async Task<bool> SaveDeviceProfileAsync(
            IList<StoredDeviceConfig> storedDevices,
            string serial,
            DeviceProfileConfig profile,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(profile);
            var storedDevice = storedDevices.FirstOrDefault(device => DeviceRowFactory.SerialEquals(device.Serial, serial));
            if (storedDevice == null)
                return false;

            void Apply(StoredDeviceConfig device) => ApplyDeviceProfile(device, profile);

            bool updated = await _deviceStoreService.UpdateAsync(serial, Apply, cancellationToken).ConfigureAwait(false);
            if (updated)
                Apply(storedDevice);

            return updated;
        }

        public async Task<bool> SaveTimezoneConfigAsync(
            string serial,
            ChangeTimezoneMode mode,
            string timezone,
            CancellationToken cancellationToken)
        {
            void Apply(StoredDeviceConfig device)
            {
                device.TimezoneMode = mode.ToString();
                device.Timezone = Normalize(timezone);
            }

            return await _deviceStoreService.UpdateAsync(serial, Apply, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<bool> SaveTimezoneConfigAsync(
            IList<StoredDeviceConfig> storedDevices,
            string serial,
            ChangeTimezoneMode mode,
            string timezone,
            CancellationToken cancellationToken)
        {
            var storedDevice = storedDevices.FirstOrDefault(device => DeviceRowFactory.SerialEquals(device.Serial, serial));
            if (storedDevice == null)
                return false;

            void Apply(StoredDeviceConfig device)
            {
                device.TimezoneMode = mode.ToString();
                device.Timezone = Normalize(timezone);
            }

            bool updated = await _deviceStoreService.UpdateAsync(serial, Apply, cancellationToken).ConfigureAwait(false);
            if (updated)
                Apply(storedDevice);

            return updated;
        }

        public async Task<bool> SaveLocationConfigAsync(
            string serial,
            ChangeLocationMode mode,
            string latitude,
            string longitude,
            CancellationToken cancellationToken)
        {
            void Apply(StoredDeviceConfig device)
            {
                device.LocationMode = mode.ToString();
                device.LocationLatitude = Normalize(latitude);
                device.LocationLongitude = Normalize(longitude);
            }

            return await _deviceStoreService.UpdateAsync(serial, Apply, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task<bool> SaveLocationConfigAsync(
            IList<StoredDeviceConfig> storedDevices,
            string serial,
            ChangeLocationMode mode,
            string latitude,
            string longitude,
            CancellationToken cancellationToken)
        {
            return SaveLocationConfigCoreAsync(
                storedDevices,
                serial,
                mode,
                latitude,
                longitude,
                countryCode: string.Empty,
                cityName: string.Empty,
                replaceMetadata: false,
                cancellationToken: cancellationToken);
        }

        public Task<bool> SaveLocationConfigAsync(
            IList<StoredDeviceConfig> storedDevices,
            string serial,
            ChangeLocationMode mode,
            string latitude,
            string longitude,
            string countryCode,
            string cityName,
            CancellationToken cancellationToken)
        {
            return SaveLocationConfigCoreAsync(
                storedDevices,
                serial,
                mode,
                latitude,
                longitude,
                countryCode,
                cityName,
                replaceMetadata: true,
                cancellationToken: cancellationToken);
        }

        private async Task<bool> SaveLocationConfigCoreAsync(
            IList<StoredDeviceConfig> storedDevices,
            string serial,
            ChangeLocationMode mode,
            string latitude,
            string longitude,
            string countryCode,
            string cityName,
            bool replaceMetadata,
            CancellationToken cancellationToken)
        {
            var storedDevice = storedDevices.FirstOrDefault(device => DeviceRowFactory.SerialEquals(device.Serial, serial));
            if (storedDevice == null)
                return false;

            void Apply(StoredDeviceConfig device)
            {
                device.LocationMode = mode.ToString();
                device.LocationLatitude = Normalize(latitude);
                device.LocationLongitude = Normalize(longitude);

                if (replaceMetadata)
                {
                    device.LocationCountryCode = Normalize(countryCode);
                    device.LocationCityName = Normalize(cityName);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(countryCode))
                        device.LocationCountryCode = Normalize(countryCode);

                    if (!string.IsNullOrWhiteSpace(cityName))
                        device.LocationCityName = Normalize(cityName);
                }
            }

            bool updated = await _deviceStoreService.UpdateAsync(serial, Apply, cancellationToken).ConfigureAwait(false);
            if (updated)
                Apply(storedDevice);

            return updated;
        }

        public async Task<bool> SaveProxyConfigAsync(
            string serial,
            FakeProxyDialogResult configuration,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            void Apply(StoredDeviceConfig device)
            {
                device.ProxyFullString = new ProxyEndpoint(
                    configuration.Host.Trim(),
                    configuration.Port,
                    configuration.Username.Trim(),
                    configuration.Password.Trim()).NormalizedText;
                device.ProxyType = ProxyEndpoint.DefaultProxyType;
            }

            return await _deviceStoreService.UpdateAsync(serial, Apply, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<bool> SaveProxyConfigAsync(
            IList<StoredDeviceConfig> storedDevices,
            string serial,
            FakeProxyDialogResult configuration,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var storedDevice = storedDevices.FirstOrDefault(device =>
                DeviceRowFactory.SerialEquals(device.Serial, serial));
            if (storedDevice == null)
                return false;

            void Apply(StoredDeviceConfig device)
            {
                device.ProxyFullString = new ProxyEndpoint(
                    configuration.Host.Trim(),
                    configuration.Port,
                    configuration.Username.Trim(),
                    configuration.Password.Trim()).NormalizedText;
                device.ProxyType = ProxyEndpoint.DefaultProxyType;
            }

            bool updated = await _deviceStoreService.UpdateAsync(serial, Apply, cancellationToken)
                .ConfigureAwait(false);
            if (updated)
                Apply(storedDevice);

            return updated;
        }

        public static void ApplySelectedCarrierConfig(
            StoredDeviceConfig storedDevice,
            CarrierCountryOption? selectedCountry,
            CarrierOption? selectedCarrier)
        {
            storedDevice.CountryIso = selectedCountry?.CountryIso ?? string.Empty;
            storedDevice.CountryName = selectedCountry?.CountryName ?? string.Empty;
            storedDevice.Carrier = selectedCarrier?.CarrierName ?? string.Empty;
            storedDevice.CarrierMcc = selectedCarrier?.Mcc ?? string.Empty;
            storedDevice.CarrierMnc = selectedCarrier?.Mnc ?? string.Empty;
        }

        private static string Normalize(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static void ApplyDeviceProfile(StoredDeviceConfig device, DeviceProfileConfig profile)
        {
            device.Brand = Normalize(profile.Brand);
            device.AndroidVersion = Normalize(profile.AndroidVersion);
            device.ChangeSimEnabled = profile.ChangeSimEnabled;
            device.UseIntegritySecurityPatch = profile.UseIntegritySecurityPatch;
            device.CountryIso = Normalize(profile.CountryIso).ToLowerInvariant();
            device.CountryName = Normalize(profile.CountryName);
            device.Carrier = Normalize(profile.Carrier);
            device.CarrierMcc = Normalize(profile.CarrierMcc);
            device.CarrierMnc = Normalize(profile.CarrierMnc);
            device.ChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(profile.ChangeOptions);
        }
    }
}
