using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceRandomProfileService : IDeviceRandomProfileService
    {
        public const string RandomOption = DeviceProfileOptions.Random;

        private const string DefaultBrand = "samsung";
        private const int DefaultSdk = 33;
        private static readonly string[] BrandsPool = { "google", "OnePlus", "OPPO", "samsung", "vivo", "Xiaomi" };
        private static readonly IReadOnlyList<string> SupportedSdkLevels =
            DeviceProfileOptions.SupportedAndroidVersions
                .Select(NormalizeSdk)
                .Select(sdk => sdk.ToString())
                .ToArray();
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> BrandOsMap =
            DeviceProfileOptions.AndroidVersionsByBrand.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.Select(NormalizeSdk).Select(sdk => sdk.ToString()).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        private static readonly IReadOnlyDictionary<string, string> BrandAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["samsung"] = "samsung",
            ["oppo"] = "OPPO",
            ["vivo"] = "vivo",
            ["google"] = "google",
            ["oneplus"] = "OnePlus",
            ["xiaomi"] = "Xiaomi"
        };

        private readonly IDeviceRandomApiService _deviceRandomApiService;
        private readonly IRandomService _randomService;
        private readonly ISimProfileService _simProfileService;

        public DeviceRandomProfileService(
            IDeviceRandomApiService deviceRandomApiService,
            IRandomService randomService,
            ISimProfileService simProfileService)
        {
            _deviceRandomApiService = deviceRandomApiService;
            _randomService = randomService;
            _simProfileService = simProfileService;
        }

        public async Task<DeviceInfoApiDevice> CreateRandomProfileAsync(AccountSession session, RandomDeviceRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var selection = SelectRandomValue(request.SelectedBrand, request.SelectedAndroidVersion);
            var device = await _deviceRandomApiService.GetRandomDeviceAsync(session, selection, cancellationToken).ConfigureAwait(false);
            NormalizeDeviceResponse(device, selection);
            ApplyGeneratedValues(device, request);
            return device;
        }

        private RandomDeviceSelection SelectRandomValue(string? brandInput, string? osInput)
        {
            var brandWasRandom = IsRandom(brandInput);
            var osWasRandom = IsRandom(osInput);
            var brand = brandWasRandom
                ? _randomService.PickRandom(BrandsPool)
                : NormalizeBrand(brandInput);

            var sdk = osWasRandom
                ? PickRandomSdkForBrand(brand)
                : NormalizeSdk(osInput);

            if (brandWasRandom && !osWasRandom)
            {
                var candidateBrands = BrandOsMap
                    .Where(pair => pair.Value.Contains(sdk.ToString()))
                    .Select(pair => pair.Key)
                    .ToArray();

                if (candidateBrands.Length > 0)
                    brand = _randomService.PickRandom(candidateBrands);
            }

            sdk = GetValidSdkForBrand(brand, sdk);
            return new RandomDeviceSelection(brand, sdk);
        }

        private void NormalizeDeviceResponse(DeviceInfoApiDevice device, RandomDeviceSelection selection)
        {
            if (string.IsNullOrWhiteSpace(device.Model))
                throw new DeviceRandomApiException("Random device result is incomplete.");

            NormalizeFingerprint(device);

            if (string.IsNullOrWhiteSpace(device.Manufacturer))
                device.Manufacturer = selection.Brand;

            if (string.IsNullOrWhiteSpace(device.Brand))
                device.Brand = NormalizeBrand(device.Manufacturer);

            if (string.IsNullOrWhiteSpace(device.Name) || string.Equals(device.Name, "unknown", StringComparison.OrdinalIgnoreCase))
                device.Name = string.IsNullOrWhiteSpace(device.Board) ? device.Code : device.Board;

            if (string.IsNullOrWhiteSpace(device.Imei))
                device.Imei = GenerateImei();

            if (string.IsNullOrWhiteSpace(device.Imei1))
                device.Imei1 = GenerateImei();

            device.Sdk = selection.Sdk.ToString();
            device.AndroidId = _randomService.GetRandomHexString(16)[..16];
            device.Serial = _randomService.GetRandomHexString(16)[.._randomService.RandomInRange(8, 13)];
            device.WifiMacAddress = _randomService.GenerateWifiMacAddress(device.Manufacturer ?? selection.Brand);
            device.BluetoothMacAddress = _randomService.GenerateMacAddress();
        }

        private void ApplyGeneratedValues(DeviceInfoApiDevice device, RandomDeviceRequest request)
        {
            SimProfile simProfile = _simProfileService.CreateRandomProfile(request.Country, request.Carrier);
            device.Imsi = simProfile.Imsi;
            device.Iccid = simProfile.Iccid;
            device.SimPhoneNumber = simProfile.PhoneNumber;
            device.SimOperatorNumeric = simProfile.OperatorNumeric;
            device.SimOperatorCountry = simProfile.OperatorCountry;
            device.SimOperatorName = simProfile.OperatorName;
        }

        private void NormalizeFingerprint(DeviceInfoApiDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.Fingerprint))
                return;

            var parts = device.Fingerprint.Split('/');
            if (parts.Length < 5)
                return;

            device.Product = parts[1];
            var versionPart = parts[2];
            var colonIndex = versionPart.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex > 0)
            {
                device.Code = versionPart[..colonIndex];
                device.Release = versionPart[(colonIndex + 1)..];
            }

            device.Sdk = device.Release switch
            {
                "13" => "33",
                "14" => "34",
                "15" => "35",
                _ => string.IsNullOrWhiteSpace(device.Sdk) ? DefaultSdk.ToString() : device.Sdk
            };
        }

        private string GenerateImei()
        {
            const string defaultTac = "35527335";
            var body = string.Concat(defaultTac, _randomService.RandomInRange(0, 999999).ToString("D6"));
            return string.Concat(body, GenerateLuhnCheckDigit(body));
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

        private int PickRandomSdkForBrand(string brand)
        {
            var validOs = BrandOsMap.TryGetValue(brand, out var osList)
                ? osList
                : SupportedSdkLevels;
            return int.Parse(_randomService.PickRandom(validOs));
        }

        private int GetValidSdkForBrand(string brand, int sdk)
        {
            if (!BrandOsMap.TryGetValue(brand, out var validOsList))
                return sdk;

            var sdkText = sdk.ToString();
            return validOsList.Contains(sdkText) ? sdk : int.Parse(_randomService.PickRandom(validOsList));
        }

        private static bool IsRandom(string? value)
        {
            return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), RandomOption, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBrand(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DefaultBrand;

            return BrandAlias.TryGetValue(value.Trim(), out var normalizedBrand)
                ? normalizedBrand
                : value.Trim();
        }

        private static int NormalizeSdk(string? value)
        {
            return value?.Trim() switch
            {
                "33" or "Android 13" => 33,
                "34" or "Android 14" => 34,
                "35" or "Android 15" => 35,
                _ => DefaultSdk
            };
        }

    }
}
