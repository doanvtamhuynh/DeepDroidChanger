using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using System.Globalization;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceRandomProfileService : IDeviceRandomProfileService
    {
        public const string RandomOption = DeviceProfileOptions.Random;

        private const string DefaultBrand = "samsung";
        private const int DefaultSdk = 33;
        private const string BuildDateFormat = "ddd MMM dd HH:mm:ss 'UTC' yyyy";
        private static readonly DateTimeOffset FallbackBuildDateStartUtc =
            new(2025, 10, 5, 0, 0, 0, TimeSpan.Zero);
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
        private readonly IDeviceIntegrityService _deviceIntegrityService;
        private readonly IRandomService _randomService;
        private readonly ISimProfileService _simProfileService;

        public DeviceRandomProfileService(
            IDeviceRandomApiService deviceRandomApiService,
            IDeviceIntegrityService deviceIntegrityService,
            IRandomService randomService,
            ISimProfileService simProfileService)
        {
            _deviceRandomApiService = deviceRandomApiService;
            _deviceIntegrityService = deviceIntegrityService;
            _randomService = randomService;
            _simProfileService = simProfileService;
        }

        public async Task<DeviceInfoApiDevice> CreateRandomProfileAsync(AccountSession session, RandomDeviceRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var selection = SelectRandomValue(request.SelectedBrand, request.SelectedAndroidVersion);
            var device = await _deviceRandomApiService.GetRandomDeviceAsync(session, selection, cancellationToken).ConfigureAwait(false);
            NormalizeDeviceResponse(device, selection);
            if (request.UseIntegritySecurityPatch)
            {
                string? integritySecurityPatch = await _deviceIntegrityService
                    .TryGetRandomSecurityPatchAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(integritySecurityPatch))
                    device.SecurityPatch = integritySecurityPatch;
            }
            NormalizeBuildDates(device);
            ApplyGeneratedValues(device, request);
            ValidateProfileForChange(device);
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
            if (IsMissingOrUnknown(device.Model))
                throw new DeviceRandomApiException("Random device result is incomplete.");

            NormalizeFingerprint(device);

            if (!string.Equals(device.Sdk, selection.Sdk.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                throw new DeviceRandomApiException("Random device result does not match the requested Android SDK.");

            if (IsMissingOrUnknown(device.Manufacturer))
                device.Manufacturer = selection.Brand;

            if (IsMissingOrUnknown(device.Brand))
                device.Brand = NormalizeBrand(device.Manufacturer);

            if (IsMissingOrUnknown(device.Name))
                device.Name = GetFirstValue(device.Code, device.Board, device.Model);

            device.Board = GetFirstValue(device.Board, device.Code, device.Product);
            device.Hardware = GetFirstValue(device.Hardware, device.Board, device.Code);
            device.Platform = GetFirstValue(device.Platform, device.Hardware, device.Board);

            if (IsMissingOrUnknown(device.Bootloader))
                device.Bootloader = device.BuildIncremental;

            if (IsMissingOrUnknown(device.Baseband))
                device.Baseband = device.BuildIncremental;

            if (IsMissingOrUnknown(device.BuildDisplayId)
                && !string.IsNullOrWhiteSpace(device.BuildId)
                && !string.IsNullOrWhiteSpace(device.BuildIncremental))
            {
                device.BuildDisplayId = string.Concat(device.BuildId, ".", device.BuildIncremental);
            }

            device.BuildFlavor = string.IsNullOrWhiteSpace(device.Product)
                ? string.Empty
                : string.Concat(device.Product, "-user");
            device.BuildDescription = CreateBuildDescription(device);
            device.BuildUser = string.Concat("android-", device.Product);

            if (IsMissingOrUnknown(device.BuildHost))
                device.BuildHost = device.BuildUser;

            string imeiBrand = GetFirstValue(device.Brand, device.Manufacturer, selection.Brand);
            if (!IsValidImei(device.Imei))
            {
                string? validServerImei1 = IsValidImei(device.Imei1) ? device.Imei1 : null;
                device.Imei = GenerateDistinctImei(
                    imeiBrand,
                    validServerImei1?[..8],
                    validServerImei1);
            }

            if (!IsValidImei(device.Imei1)
                || string.Equals(device.Imei, device.Imei1, StringComparison.Ordinal))
            {
                device.Imei1 = GenerateDistinctImei(imeiBrand, device.Imei![..8], device.Imei);
            }

            device.AndroidId = _randomService.GetRandomHexString(16)[..16];
            device.Serial = _randomService.GetRandomHexString(16)[.._randomService.RandomInRange(8, 13)];
            device.WifiMacAddress = _randomService.GenerateWifiMacAddress(device.Manufacturer ?? selection.Brand);
            device.BluetoothMacAddress = _randomService.GenerateWifiMacAddress(device.Manufacturer ?? selection.Brand);
            string deviceNamePrefix = GetFirstValue(device.Model, device.Manufacturer, "Android");
            device.SettingDeviceName = CreateRandomDeviceName(deviceNamePrefix);
            device.SettingBluetoothName = CreateRandomDeviceName(deviceNamePrefix);
            device.WifiBssid = _randomService.GenerateWifiMacAddress(device.Manufacturer ?? selection.Brand);
            device.WifiSsid = CreateRandomDeviceName(deviceNamePrefix);
            device.VbmetaDigest = _randomService.GetRandomHexString(64)[..64];
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
                throw new DeviceRandomApiException("Random device fingerprint is missing.");

            var parts = device.Fingerprint.Split('/');
            if (parts.Length < 5)
                throw new DeviceRandomApiException("Random device fingerprint is invalid.");

            device.Product = parts[1].Trim();
            var versionPart = parts[2];
            var colonIndex = versionPart.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 0 || colonIndex == versionPart.Length - 1)
                throw new DeviceRandomApiException("Random device fingerprint version is invalid.");

            device.Code = versionPart[..colonIndex].Trim();
            device.Release = versionPart[(colonIndex + 1)..].Trim();

            device.BuildId = parts[3].Trim();
            int buildVariantIndex = parts[4].IndexOf(':', StringComparison.Ordinal);
            device.BuildIncremental = buildVariantIndex > 0
                ? parts[4][..buildVariantIndex].Trim()
                : parts[4].Trim();

            if (string.IsNullOrWhiteSpace(device.Product)
                || string.IsNullOrWhiteSpace(device.Code)
                || string.IsNullOrWhiteSpace(device.BuildId)
                || string.IsNullOrWhiteSpace(device.BuildIncremental))
            {
                throw new DeviceRandomApiException("Random device fingerprint is incomplete.");
            }

            device.Sdk = device.Release switch
            {
                "13" => "33",
                "14" => "34",
                "15" => "35",
                _ => string.IsNullOrWhiteSpace(device.Sdk) ? DefaultSdk.ToString() : device.Sdk
            };
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

        private static string CreateBuildDescription(DeviceInfoApiDevice device)
        {
            string value = string.Join(
                " ",
                new[] { device.Product, device.Release, device.BuildId, device.BuildIncremental }
                    .Where(item => !string.IsNullOrWhiteSpace(item)));
            return value.Length == 0 ? string.Empty : string.Concat(value, " release-keys");
        }

        private void NormalizeBuildDates(DeviceInfoApiDevice device)
        {
            DateTimeOffset buildDateUtc;
            if (!TryParseBuildDateUtc(device.BuildDateUtc, out buildDateUtc))
                buildDateUtc = GenerateFallbackBuildDateUtc(device.SecurityPatch);

            device.BuildDateUtc = buildDateUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            device.BuildDate = buildDateUtc.ToString(BuildDateFormat, CultureInfo.InvariantCulture);
        }

        private DateTimeOffset GenerateFallbackBuildDateUtc(string? securityPatch)
        {
            DateTimeOffset start = FallbackBuildDateStartUtc;
            if (DateTime.TryParseExact(
                    securityPatch?.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime securityPatchDate))
            {
                DateTimeOffset securityPatchStart = new DateTimeOffset(securityPatchDate).AddDays(3);
                if (securityPatchStart > start)
                    start = securityPatchStart;
            }

            DateTimeOffset end = DateTimeOffset.UtcNow.AddHours(-3);
            long totalSeconds = Math.Max(0, (long)(end - start).TotalSeconds);
            if (totalSeconds == 0)
                return start;

            long randomOffset = _randomService.RandomInRange(0L, totalSeconds);
            return start.AddSeconds(randomOffset);
        }

        private static bool TryParseBuildDateUtc(string? value, out DateTimeOffset buildDateUtc)
        {
            buildDateUtc = default;
            if (!long.TryParse(
                    value?.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long unixSeconds))
            {
                return false;
            }

            try
            {
                buildDateUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static string GetFirstValue(params string?[] values)
        {
            return values.FirstOrDefault(value => !IsMissingOrUnknown(value))?.Trim() ?? string.Empty;
        }

        private static bool IsMissingOrUnknown(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            string normalized = value.Trim();
            return string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "n/a", StringComparison.OrdinalIgnoreCase);
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
                (nameof(device.Bootloader), device.Bootloader),
                (nameof(device.Baseband), device.Baseband),
                (nameof(device.SecurityPatch), device.SecurityPatch),
                (nameof(device.Serial), device.Serial),
                (nameof(device.AndroidId), device.AndroidId),
                (nameof(device.WifiMacAddress), device.WifiMacAddress),
                (nameof(device.BluetoothMacAddress), device.BluetoothMacAddress),
                (nameof(device.SettingDeviceName), device.SettingDeviceName),
                (nameof(device.SettingBluetoothName), device.SettingBluetoothName),
                (nameof(device.WifiBssid), device.WifiBssid),
                (nameof(device.WifiSsid), device.WifiSsid),
                (nameof(device.VbmetaDigest), device.VbmetaDigest),
                (nameof(device.Imsi), device.Imsi),
                (nameof(device.Iccid), device.Iccid),
                (nameof(device.SimPhoneNumber), device.SimPhoneNumber),
                (nameof(device.SimOperatorNumeric), device.SimOperatorNumeric),
                (nameof(device.SimOperatorCountry), device.SimOperatorCountry),
                (nameof(device.SimOperatorName), device.SimOperatorName)
            ];

            (string Name, string? Value) missing = requiredValues.FirstOrDefault(item => IsMissingOrUnknown(item.Value));
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
