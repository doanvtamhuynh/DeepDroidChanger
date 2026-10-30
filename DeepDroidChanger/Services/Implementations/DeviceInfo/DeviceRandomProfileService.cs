using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using System.Globalization;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceRandomProfileService : IDeviceRandomProfileService
    {
        private readonly IDeviceRandomApiService _deviceSource;
        private readonly IDeviceIntegrityService _deviceIntegrityService;
        private readonly IRandomService _randomService;
        private readonly ISimProfileService _simProfileService;

        public DeviceRandomProfileService(
            IDeviceRandomApiService deviceRandomApiService,
            IDeviceIntegrityService deviceIntegrityService,
            IRandomService randomService,
            ISimProfileService simProfileService)
        {
            _deviceSource = deviceRandomApiService;
            _deviceIntegrityService = deviceIntegrityService;
            _randomService = randomService;
            _simProfileService = simProfileService;
        }

        public async Task<DeviceInfoApiDevice> CreateRandomProfileAsync(
            RandomDeviceRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var selection = CreateSelection(request.SelectedBrand, request.SelectedAndroidVersion);
            var device = await _deviceSource
                .GetRandomDeviceAsync(selection, cancellationToken)
                .ConfigureAwait(false);
            DeviceProfileNormalizer.NormalizeIdentity(device);
            ValidateSelection(device, selection);
            ApplyGeneratedIdentity(device);
            if (request.UseIntegritySecurityPatch)
            {
                string? integritySecurityPatch = await _deviceIntegrityService
                    .TryGetRandomSecurityPatchAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(integritySecurityPatch))
                    device.SecurityPatch = integritySecurityPatch;
            }
            DeviceProfileNormalizer.NormalizeBuildDates(device, _randomService);
            ApplySimProfile(device, request);
            ValidateProfileForChange(device);
            return device;
        }

        private static RandomDeviceSelection CreateSelection(string? brandInput, string? osInput)
        {
            string brand = IsRandom(brandInput) ? "Random" : brandInput!.Trim();
            if (IsRandom(osInput))
                return new RandomDeviceSelection(brand, 0);

            string version = osInput!.Trim();
            if (version.StartsWith("Android ", StringComparison.OrdinalIgnoreCase))
            {
                string release = version[8..].Trim();
                if (release.Length == 0)
                    throw new DeviceRandomApiException("Android release is missing.");
                return new RandomDeviceSelection(brand, 0) { Release = release };
            }

            if (int.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out int sdk) && sdk > 0)
                return new RandomDeviceSelection(brand, sdk);
            throw new DeviceRandomApiException("Android version selection is invalid.");
        }

        private static void ValidateSelection(DeviceInfoApiDevice device, RandomDeviceSelection selection)
        {
            if ((selection.Sdk > 0 && device.Sdk != selection.Sdk.ToString(CultureInfo.InvariantCulture))
                || (selection.Release != null && device.Release != selection.Release)
                || (!IsRandom(selection.Brand)
                    && !device.Brand.Equals(selection.Brand, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DeviceRandomApiException("Random device result does not match the requested selection.");
            }
        }

        private void ApplyGeneratedIdentity(DeviceInfoApiDevice device)
        {
            if (DeviceProfileNormalizer.IsMissing(device.BuildUser))
                device.BuildUser = "android-" + _randomService.GetRandomHexString(8);
            if (DeviceProfileNormalizer.IsMissing(device.BuildHost))
                device.BuildHost = "build-" + _randomService.GetRandomHexString(12);

            device.Imei = GenerateDistinctImei(device.Brand, null, null);
            device.Imei1 = GenerateDistinctImei(device.Brand, device.Imei[..8], device.Imei);
            device.Serial = _randomService.GetRandomHexString(16)[.._randomService.RandomInRange(8, 13)];
            device.WifiMacAddress = _randomService.GenerateWifiMacAddress(device.Brand);
            device.BluetoothMacAddress = _randomService.GenerateWifiMacAddress(device.Brand);
            string deviceNamePrefix = DeviceProfileNormalizer.First(device.Model, device.Manufacturer, "Android");
            device.SettingDeviceName = CreateRandomDeviceName(deviceNamePrefix);
            device.SettingBluetoothName = CreateRandomDeviceName(deviceNamePrefix);
            device.WifiBssid = _randomService.GenerateWifiMacAddress(device.Brand);
            device.WifiSsid = CreateRandomDeviceName(deviceNamePrefix);
            device.VbmetaDigest = _randomService.GetRandomHexString(64)[..64];
            device.BootKey = _randomService.GetRandomHexString(64)[..64];
        }

        private void ApplySimProfile(DeviceInfoApiDevice device, RandomDeviceRequest request)
        {
            SimProfile simProfile = _simProfileService.CreateRandomProfile(request.Country, request.Carrier);
            device.Imsi = simProfile.Imsi;
            device.Iccid = simProfile.Iccid;
            device.SimPhoneNumber = simProfile.PhoneNumber;
            device.SimOperatorNumeric = simProfile.OperatorNumeric;
            device.SimOperatorCountry = simProfile.OperatorCountry;
            device.SimOperatorName = simProfile.OperatorName;
        }

        private string GenerateDistinctImei(string brand, string? preferredTac, string? excludedImei)
        {
            const int maximumAttempts = 16;
            for (var attempt = 0; attempt < maximumAttempts; attempt++)
            {
                string candidate = _randomService.GenerateImei(brand, preferredTac);
                if (IsValidImei(candidate)
                    && !string.Equals(candidate, excludedImei, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            throw new DeviceRandomApiException("Unable to generate distinct valid IMEI values.");
        }

        private string CreateRandomDeviceName(string prefix)
        {
            string normalizedPrefix = string.Concat(prefix.Where(char.IsLetterOrDigit));
            if (normalizedPrefix.Length == 0)
                normalizedPrefix = "Android";

            string randomName = _randomService.GenerateName().Trim();
            if (randomName.Length == 0)
                randomName = "Android";

            return string.Concat(normalizedPrefix, "_", randomName);
        }

        private static bool IsValidImei(string? value)
        {
            return value is { Length: 15 }
                && value.All(char.IsDigit)
                && value[14] - '0' == GenerateLuhnCheckDigit(value[..14]);
        }

        private static void ValidateProfileForChange(DeviceInfoApiDevice device)
        {
            (string Name, string? Value)[] requiredValues =
            [
                (nameof(device.Model), device.Model),
                (nameof(device.Fingerprint), device.Fingerprint),
                (nameof(device.Manufacturer), device.Manufacturer),
                (nameof(device.Brand), device.Brand),
                (nameof(device.Product), device.Product),
                (nameof(device.Code), device.Code),
                (nameof(device.Name), device.Name),
                (nameof(device.Release), device.Release),
                (nameof(device.Sdk), device.Sdk),
                (nameof(device.BuildId), device.BuildId),
                (nameof(device.BuildIncremental), device.BuildIncremental),
                (nameof(device.BuildDisplayId), device.BuildDisplayId),
                (nameof(device.BuildDescription), device.BuildDescription),
                (nameof(device.BuildFlavor), device.BuildFlavor),
                (nameof(device.BuildUser), device.BuildUser),
                (nameof(device.BuildHost), device.BuildHost),
                (nameof(device.BuildDate), device.BuildDate),
                (nameof(device.BuildDateUtc), device.BuildDateUtc),
                (nameof(device.Board), device.Board),
                (nameof(device.Hardware), device.Hardware),
                (nameof(device.Platform), device.Platform),
                (nameof(device.SocManufacturer), device.SocManufacturer),
                (nameof(device.SocModel), device.SocModel),
                (nameof(device.Bootloader), device.Bootloader),
                (nameof(device.Baseband), device.Baseband),
                (nameof(device.SecurityPatch), device.SecurityPatch),
                (nameof(device.Serial), device.Serial),
                (nameof(device.WifiMacAddress), device.WifiMacAddress),
                (nameof(device.BluetoothMacAddress), device.BluetoothMacAddress),
                (nameof(device.SettingDeviceName), device.SettingDeviceName),
                (nameof(device.SettingBluetoothName), device.SettingBluetoothName),
                (nameof(device.WifiBssid), device.WifiBssid),
                (nameof(device.WifiSsid), device.WifiSsid),
                (nameof(device.VbmetaDigest), device.VbmetaDigest),
                (nameof(device.BootKey), device.BootKey),
                (nameof(device.Imsi), device.Imsi),
                (nameof(device.Iccid), device.Iccid),
                (nameof(device.SimPhoneNumber), device.SimPhoneNumber),
                (nameof(device.SimOperatorNumeric), device.SimOperatorNumeric),
                (nameof(device.SimOperatorCountry), device.SimOperatorCountry),
                (nameof(device.SimOperatorName), device.SimOperatorName)
            ];

            (string Name, string? Value) missing = requiredValues.FirstOrDefault(item => DeviceProfileNormalizer.IsMissing(item.Value));
            if (missing.Name != null)
                throw new DeviceRandomApiException($"Random device result is incomplete: {missing.Name} is missing.");

            if (!IsValidImei(device.Imei)
                || !IsValidImei(device.Imei1)
                || string.Equals(device.Imei, device.Imei1, StringComparison.Ordinal))
            {
                throw new DeviceRandomApiException("Random device result contains invalid IMEI values.");
            }
        }

        private static int GenerateLuhnCheckDigit(string body)
        {
            var sum = 0;
            for (var index = 0; index < body.Length; index++)
            {
                var digit = body[index] - '0';
                if ((index + 1) % 2 == 0)
                {
                    digit *= 2;
                    if (digit > 9)
                        digit -= 9;
                }

                sum += digit;
            }

            return (10 - sum % 10) % 10;
        }

        private static bool IsRandom(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), "Random", StringComparison.OrdinalIgnoreCase);
        }
    }
}
