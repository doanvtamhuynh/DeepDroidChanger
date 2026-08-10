using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DeepDroidChanger.Services;

public sealed class DeviceChangeService : IDeviceChangeService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAdbCommandService _adb;
    private readonly IDeviceDataCleanupService _cleanupService;
    private readonly IDeviceIntegrityService _integrityService;
    private readonly IDeviceLocationService? _locationService;
    private readonly IDeviceTimezoneService? _timezoneService;
    private readonly ILocationDataService? _locationDataService;
    private readonly IRandomService? _randomService;
    private readonly ILogger<DeviceChangeService> _logger;

    public DeviceChangeService(
        IAdbCommandService adb,
        IDeviceDataCleanupService cleanupService,
        IDeviceIntegrityService integrityService,
        ILogger<DeviceChangeService> logger)
        : this(adb, cleanupService, integrityService, null, null, null, null, logger)
    {
    }

    public DeviceChangeService(
        IAdbCommandService adb,
        IDeviceDataCleanupService cleanupService,
        IDeviceIntegrityService integrityService,
        IDeviceLocationService? locationService,
        IDeviceTimezoneService? timezoneService,
        ILocationDataService? locationDataService,
        IRandomService? randomService,
        ILogger<DeviceChangeService> logger)
    {
        _adb = adb;
        _cleanupService = cleanupService;
        _integrityService = integrityService;
        _locationService = locationService;
        _timezoneService = timezoneService;
        _locationDataService = locationDataService;
        _randomService = randomService;
        _logger = logger;
    }

    public async Task ChangeSimAsync(
        string serial,
        SimProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ValidateSimProfile(profile);
        await ExecuteWithDeviceLockAsync(serial, async () =>
        {
            await EnsureRootAsync(serial, cancellationToken).ConfigureAwait(false);
            await SetPropertiesAsync(serial, CreateSimProperties(profile), cancellationToken).ConfigureAwait(false);
            await RunRequiredShellAsync(
                    serial,
                    "sync",
                    "sync changed SIM information",
                    cancellationToken)
                .ConfigureAwait(false);
            await RebootAndWaitAsync(serial, progress: null, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Changed SIM information and rebooted device {Serial}.", serial);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeWithoutWipeAsync(
        string serial,
        DeviceInfoApiDevice profile,
        bool changeSim,
        DeviceChangeOptions options,
        IProgress<DeviceChangeStage>? progress,
        CancellationToken cancellationToken)
    {
        Validate(serial, profile, options);
        await ExecuteWithDeviceLockAsync(serial, async () =>
        {
            progress?.Report(DeviceChangeStage.Preparing);
            await EnsureRootAsync(serial, cancellationToken).ConfigureAwait(false);

            bool changeAndroidId = ShouldChangeAndroidId(options);
            string? originalAndroidId = changeAndroidId
                ? await ReadAndroidIdAsync(serial, cancellationToken).ConfigureAwait(false)
                : null;
            bool changeMacAddress = options.UseDefaultMode || options.ChangeMacAddress;
            if (changeMacAddress)
            {
                await _adb.SetWifiAsync(serial, false, cancellationToken).ConfigureAwait(false);
                await RunRequiredShellAsync(
                        serial,
                        "svc bluetooth disable",
                        "disable Bluetooth before changing identity",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await _cleanupService.DeleteSsaidAsync(serial, cancellationToken).ConfigureAwait(false);
            if (changeAndroidId)
                await DeleteAndroidIdSettingAsync(serial, cancellationToken).ConfigureAwait(false);

            bool updateIntegrity = ShouldUpdateIntegrity(options);
            bool changeLocation = ShouldChangeLocation(options);
            bool changeTimezone = ShouldChangeTimezone(options);
            progress?.Report(DeviceChangeStage.ApplyingProfile);
            await ApplyProfileAsync(
                    serial,
                    profile,
                    changeSim,
                    changeMacAddress,
                    updateIntegrity,
                    changeLocation,
                    changeTimezone,
                    cancellationToken)
                .ConfigureAwait(false);

            await RebootAndWaitAsync(serial, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(DeviceChangeStage.Verifying);
            await VerifyAppliedProfileAsync(
                    serial,
                    profile,
                    cancellationToken)
                .ConfigureAwait(false);
            if (changeAndroidId)
            {
                await VerifyRegeneratedAndroidIdAsync(
                        serial,
                        originalAndroidId!,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(DeviceChangeStage.Completed);
            _logger.LogInformation(
                "Changed device identity without wiping package data on {Serial}. Default mode: {DefaultMode}; Android ID changed: {AndroidIdChanged}; SIM changed: {SimChanged}; MAC changed: {MacChanged}.",
                serial,
                options.UseDefaultMode,
                changeAndroidId,
                changeSim,
                changeMacAddress);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task WipeWithoutChangeAsync(
        string serial,
        DeviceChangeOptions options,
        IProgress<DeviceChangeStage>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(options);
        await ExecuteWithDeviceLockAsync(serial, async () =>
        {
            progress?.Report(DeviceChangeStage.Preparing);
            await EnsureRootAsync(serial, cancellationToken).ConfigureAwait(false);
            progress?.Report(DeviceChangeStage.ClearingData);
            await _cleanupService
                .CleanPreservingSsaidAsync(serial, options, cancellationToken)
                .ConfigureAwait(false);
            await RebootAndWaitAsync(serial, progress, cancellationToken).ConfigureAwait(false);
            await EnsureRootAsync(serial, cancellationToken).ConfigureAwait(false);
            await _cleanupService
                .CleanPostRebootAsync(serial, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(DeviceChangeStage.Completed);
            _logger.LogWarning(
                "Wiped device data without changing identity on {Serial} while preserving SSAID. Default mode: {DefaultMode}; package cleanup: {PackageCleanup}.",
                serial,
                options.UseDefaultMode,
                DeviceChangeOptionsHelper.HasPackageCleanup(options));
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeAsync(
        string serial,
        DeviceInfoApiDevice profile,
        bool changeSim,
        DeviceChangeOptions options,
        IProgress<DeviceChangeStage>? progress,
        CancellationToken cancellationToken)
    {
        Validate(serial, profile, options);
        await ExecuteWithDeviceLockAsync(serial, async () =>
        {
            progress?.Report(DeviceChangeStage.Preparing);
            await EnsureRootAsync(serial, cancellationToken).ConfigureAwait(false);
            await _adb.SetWifiAsync(serial, false, cancellationToken).ConfigureAwait(false);

            bool changeAndroidId = ShouldChangeAndroidId(options);
            string? originalAndroidId = changeAndroidId
                ? await ReadAndroidIdAsync(serial, cancellationToken).ConfigureAwait(false)
                : null;
            bool changeMacAddress = options.UseDefaultMode || options.ChangeMacAddress;
            if (changeMacAddress)
            {
                await RunRequiredShellAsync(
                        serial,
                        "svc bluetooth disable",
                        "disable Bluetooth before changing identity",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            bool updateIntegrity = ShouldUpdateIntegrity(options);
            bool changeLocation = ShouldChangeLocation(options);
            bool changeTimezone = ShouldChangeTimezone(options);
            progress?.Report(DeviceChangeStage.ApplyingProfile);
            await ApplyProfileAsync(
                    serial,
                    profile,
                    changeSim,
                    changeMacAddress,
                    updateIntegrity,
                    changeLocation,
                    changeTimezone,
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(DeviceChangeStage.ClearingData);
            await _cleanupService.CleanAsync(serial, options, cancellationToken).ConfigureAwait(false);
            if (changeAndroidId)
                await DeleteAndroidIdSettingAsync(serial, cancellationToken).ConfigureAwait(false);

            await RebootAndWaitAsync(serial, progress, cancellationToken).ConfigureAwait(false);
            await EnsureRootAsync(serial, cancellationToken).ConfigureAwait(false);
            await _cleanupService
                .CleanPostRebootAsync(serial, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(DeviceChangeStage.Verifying);
            await VerifyAppliedProfileAsync(
                    serial,
                    profile,
                    cancellationToken)
                .ConfigureAwait(false);
            if (changeAndroidId)
            {
                await VerifyRegeneratedAndroidIdAsync(
                        serial,
                        originalAndroidId!,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(DeviceChangeStage.Completed);
            _logger.LogInformation(
                "Changed device identity and rebooted device {Serial}. Default mode: {DefaultMode}; Android ID changed: {AndroidIdChanged}; package cleanup: {PackageCleanup}.",
                serial,
                options.UseDefaultMode,
                changeAndroidId,
                DeviceChangeOptionsHelper.HasPackageCleanup(options));
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteWithDeviceLockAsync(
        string serial,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim deviceLock = _deviceLocks.GetOrAdd(serial, _ => new SemaphoreSlim(1, 1));
        await deviceLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            deviceLock.Release();
        }
    }

    private async Task RebootAndWaitAsync(
        string serial,
        IProgress<DeviceChangeStage>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(DeviceChangeStage.Rebooting);
        await _adb.RebootAsync(serial, cancellationToken).ConfigureAwait(false);
        progress?.Report(DeviceChangeStage.WaitingForDevice);
        await WaitForBootCompletedAsync(serial, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureRootAsync(string serial, CancellationToken cancellationToken)
    {
        CommandResult rootResult = await _adb
            .RunAdbAsync(serial, "root", cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(rootResult, serial, "restart adbd as root");

        CommandResult waitResult = await _adb
            .RunAdbAsync(serial, "wait-for-device", cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(waitResult, serial, "wait for rooted device");

        CommandResult identityResult = await _adb
            .RunAdbShellAsync(serial, "id -u", cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(identityResult, serial, "verify root access");
        if (!string.Equals(
                identityResult.StandardOutput.Trim(),
                "0",
                StringComparison.Ordinal))
            throw new InvalidOperationException($"Device {serial} does not provide ADB root access.");
    }

    private async Task ApplyProfileAsync(
        string serial,
        DeviceInfoApiDevice profile,
        bool changeSim,
        bool changeMacAddress,
        bool updateIntegrity,
        bool changeLocation,
        bool changeTimezone,
        CancellationToken cancellationToken)
    {
        if (updateIntegrity)
        {
            await _integrityService.ApplyAsync(
                    serial,
                    new UpdateIntegrityDialogResult(
                        updateIntegrityFromServer: true,
                        updateIntegrityEnabled: true,
                        updateKeyboxEnabled: true,
                        updateIntegrityFile: string.Empty,
                        updateKeyboxFile: string.Empty),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await ApplyLocationAndTimezoneAsync(
                serial,
                profile,
                changeLocation,
                changeTimezone,
                cancellationToken)
            .ConfigureAwait(false);

        await SetPropertiesAsync(
                serial,
                CreateIdentityProperties(profile, changeMacAddress),
                cancellationToken)
            .ConfigureAwait(false);
        await SetPropertiesAsync(serial, CreateSimProperties(profile, changeSim), cancellationToken)
            .ConfigureAwait(false);

        string deviceName = FirstValue(profile.SettingDeviceName, profile.Name, profile.Model);
        string bluetoothName = FirstValue(profile.SettingBluetoothName, deviceName);
        await _adb.PutSettingAsync(
                serial,
                DeviceSettingsInfoConstants.GlobalNamespace,
                DeviceSettingsInfoConstants.DeviceName,
                deviceName,
                cancellationToken)
            .ConfigureAwait(false);
        await _adb.PutSettingAsync(
                serial,
                DeviceSettingsInfoConstants.SecureNamespace,
                DeviceSettingsInfoConstants.BluetoothName,
                bluetoothName,
                cancellationToken)
            .ConfigureAwait(false);
        await _adb.PutSettingAsync(
                serial,
                DeviceSettingsInfoConstants.GlobalNamespace,
                DeviceSettingsInfoConstants.WifiP2pDeviceName,
                deviceName,
                cancellationToken)
            .ConfigureAwait(false);
        if (changeMacAddress)
        {
            await _adb.DeleteSettingAsync(
                    serial,
                    DeviceSettingsInfoConstants.SecureNamespace,
                    DeviceSettingsInfoConstants.BluetoothAddress,
                    cancellationToken)
                .ConfigureAwait(false);
            await _adb.DeleteSettingAsync(
                    serial,
                    DeviceSettingsInfoConstants.SecureNamespace,
                    DeviceSettingsInfoConstants.BluetoothAddressValid,
                    cancellationToken)
                .ConfigureAwait(false);
            await _adb.PutSettingAsync(
                    serial,
                    DeviceSettingsInfoConstants.GlobalNamespace,
                    DeviceSettingsInfoConstants.RandomMac,
                    "1",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _adb.PutSettingAsync(
                serial,
                DeviceSettingsInfoConstants.SystemNamespace,
                DeviceSettingsInfoConstants.ScreenTimeout,
                "1800000",
                cancellationToken)
            .ConfigureAwait(false);
        await RunRequiredShellAsync(
                serial,
                "locksettings set-disabled true",
                "disable lock screen",
                cancellationToken)
            .ConfigureAwait(false);
        await RunRequiredShellAsync(
                serial,
                "sync",
                "sync changed identity",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SetPropertiesAsync(
        string serial,
        IEnumerable<KeyValuePair<string, string>> properties,
        CancellationToken cancellationToken)
    {
        foreach ((string propertyName, string value) in properties)
            await _adb.SetPropertyAsync(serial, propertyName, value, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> CreateIdentityProperties(
        DeviceInfoApiDevice profile,
        bool changeMacAddress)
    {
        List<KeyValuePair<string, string>> properties =
        [
            Pair(PropertyConstants.Spoof.ProductBrand, profile.Brand),
            Pair(PropertyConstants.Spoof.ProductDevice, profile.Code),
            Pair(PropertyConstants.Spoof.ProductManufacturer, profile.Manufacturer),
            Pair(PropertyConstants.Spoof.ProductModel, profile.Model),
            Pair(PropertyConstants.Spoof.ProductName, profile.Name),
            Pair(PropertyConstants.Spoof.BuildFingerprint, profile.Fingerprint),
            Pair(PropertyConstants.Spoof.BuildId, profile.BuildId),
            Pair(PropertyConstants.Spoof.BuildIncremental, profile.BuildIncremental),
            Pair(PropertyConstants.Spoof.BuildDate, profile.BuildDate),
            Pair(PropertyConstants.Spoof.BuildDateUtc, profile.BuildDateUtc),
            Pair(PropertyConstants.Spoof.BuildUser, profile.BuildUser),
            Pair(PropertyConstants.Spoof.BuildHost, profile.BuildHost),
            Pair(PropertyConstants.Spoof.BuildFlavor, profile.BuildFlavor),
            Pair(PropertyConstants.Spoof.BuildProduct, profile.Product),
            Pair(PropertyConstants.Spoof.Hardware, profile.Hardware),
            Pair(PropertyConstants.Spoof.Board, profile.Board),
            Pair(PropertyConstants.Spoof.Platform, profile.Platform),
            Pair(PropertyConstants.Spoof.Bootloader, profile.Bootloader),
            Pair(PropertyConstants.Spoof.SocManufacturer, profile.Manufacturer),
            Pair(PropertyConstants.Spoof.SocModel, profile.Hardware),
            Pair(PropertyConstants.Spoof.SecurityPatch, profile.SecurityPatch),
            Pair(PropertyConstants.Spoof.AndroidRelease, profile.Release),
            Pair(PropertyConstants.Spoof.BuildDisplayId, profile.BuildDisplayId),
            Pair(PropertyConstants.Spoof.BuildDescription, profile.BuildDescription),
            Pair(PropertyConstants.Spoof.ClientIdBase, string.Concat("android-", profile.Brand)),
            Pair(PropertyConstants.Spoof.Baseband, profile.Baseband),
            Pair(PropertyConstants.Spoof.SerialNumber, profile.Serial),
            Pair(PropertyConstants.Spoof.DeviceName, FirstValue(profile.SettingDeviceName, profile.Name, profile.Model)),
            Pair(PropertyConstants.Spoof.VbmetaDigest, profile.VbmetaDigest),
            Pair(PropertyConstants.Spoof.Imei0, profile.Imei),
            Pair(PropertyConstants.Spoof.Imei1, profile.Imei1),
            Pair(PropertyConstants.Spoof.BluetoothName, profile.SettingBluetoothName),
            Pair(PropertyConstants.Spoof.WifiSsid, profile.WifiSsid)
        ];

        if (changeMacAddress)
        {
            properties.Add(Pair(PropertyConstants.Spoof.BluetoothMac, profile.BluetoothMacAddress));
            properties.Add(Pair(PropertyConstants.Spoof.WifiMac, profile.WifiMacAddress));
            properties.Add(Pair(PropertyConstants.Spoof.WifiBssid, profile.WifiBssid));
        }

        return properties;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> CreateSimProperties(
        DeviceInfoApiDevice profile,
        bool changeSim)
    {
        return CreateSimProperties(
            changeSim,
            changeSim ? profile.Iccid : string.Empty,
            changeSim ? profile.Imsi : string.Empty,
            changeSim ? profile.SimPhoneNumber : string.Empty,
            changeSim ? profile.SimOperatorName : string.Empty,
            changeSim ? profile.SimOperatorCountry : string.Empty,
            changeSim ? profile.SimOperatorNumeric : string.Empty);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> CreateSimProperties(SimProfile profile)
    {
        return CreateSimProperties(
            true,
            profile.Iccid,
            profile.Imsi,
            profile.PhoneNumber,
            profile.OperatorName,
            profile.OperatorCountry,
            profile.OperatorNumeric);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> CreateSimProperties(
        bool enabled,
        string iccid,
        string imsi,
        string phoneNumber,
        string operatorName,
        string operatorCountry,
        string operatorNumeric)
    {
        return
        [
            Pair(
                PropertyConstants.Spoof.SimEnabled,
                enabled ? "1" : "0"),
            Pair(PropertyConstants.Spoof.SimIccid, iccid),
            Pair(PropertyConstants.Spoof.SimImsi, imsi),
            Pair(PropertyConstants.Spoof.SimPhoneNumber, phoneNumber),
            Pair(PropertyConstants.Spoof.SimOperatorName, operatorName),
            Pair(PropertyConstants.Spoof.SimOperatorCountry, operatorCountry),
            Pair(PropertyConstants.Spoof.SimOperatorNumeric, operatorNumeric),
            Pair(PropertyConstants.Spoof.Sim2Enabled, "0"),
            Pair(PropertyConstants.Spoof.Sim2Iccid, string.Empty),
            Pair(PropertyConstants.Spoof.Sim2Imsi, string.Empty),
            Pair(PropertyConstants.Spoof.Sim2PhoneNumber, string.Empty)
        ];
    }

    private async Task WaitForBootCompletedAsync(string serial, CancellationToken cancellationToken)
    {
        CommandResult waitResult = await _adb
            .RunAdbAsync(serial, "wait-for-device", cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(waitResult, serial, "wait for rebooted device");

        for (int attempt = 0; attempt < 90; attempt++)
        {
            CommandResult bootResult = await _adb
                .RunAdbShellAsync(serial, "getprop sys.boot_completed", cancellationToken)
                .ConfigureAwait(false);
            if (bootResult.ExitCode == 0
                && string.Equals(
                    bootResult.StandardOutput.Trim(),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Device {serial} did not finish booting after Change Device.");
    }

    private static bool ShouldChangeAndroidId(DeviceChangeOptions options)
    {
        return !options.UseDefaultMode && options.ChangeAndroidId;
    }

    private static bool ShouldUpdateIntegrity(DeviceChangeOptions options)
    {
        return !options.UseDefaultMode && options.UpdateIntegrity;
    }

    private static bool ShouldChangeLocation(DeviceChangeOptions options)
    {
        return !options.UseDefaultMode && options.ChangeLocation;
    }

    private static bool ShouldChangeTimezone(DeviceChangeOptions options)
    {
        return !options.UseDefaultMode && options.ChangeTimezone;
    }

    private async Task ApplyLocationAndTimezoneAsync(
        string serial,
        DeviceInfoApiDevice profile,
        bool changeLocation,
        bool changeTimezone,
        CancellationToken cancellationToken)
    {
        if ((!changeLocation && !changeTimezone) || _locationDataService == null)
            return;

        try
        {
            var locations = await _locationDataService.GetLocationsAsync(cancellationToken).ConfigureAwait(false);
            if (locations.Count == 0)
                return;

            string countryIso = profile.SimOperatorCountry;
            IReadOnlyList<LocationOption> countryLocations = string.IsNullOrWhiteSpace(countryIso)
                ? Array.Empty<LocationOption>()
                : locations.Where(loc => string.Equals(loc.CountryCode, countryIso, StringComparison.OrdinalIgnoreCase)).ToList();

            var targetLocations = countryLocations.Count > 0 ? countryLocations : locations;
            IRandomService random = _randomService ?? new RandomService();

            if (changeLocation && _locationService != null)
            {
                LocationOption selectedLocation = random.PickRandom(targetLocations);
                string latitude = LocationCoordinateRandomizer.RandomizeLatitude(
                    selectedLocation.Latitude,
                    random);
                string longitude = LocationCoordinateRandomizer.RandomizeLongitude(
                    selectedLocation.Longitude,
                    random);

                await _locationService.ApplyLocationAsync(
                        serial,
                        latitude,
                        longitude,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (changeTimezone && _timezoneService != null)
            {
                var countryTimezones = targetLocations
                    .Select(loc => loc.Timezone)
                    .Where(tz => !string.IsNullOrWhiteSpace(tz))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string selectedTimezone = countryTimezones.Count > 0
                    ? random.PickRandom(countryTimezones)
                    : random.PickRandom(targetLocations).Timezone;

                await _timezoneService.ApplyTimezoneAsync(
                        serial,
                        selectedTimezone,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply location or timezone during change device for {Serial}.", serial);
        }
    }

    private Task<string> ReadAndroidIdAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        return _adb.GetSettingAsync(
            serial,
            DeviceSettingsInfoConstants.SecureNamespace,
            DeviceSettingsInfoConstants.AndroidId,
            cancellationToken);
    }

    private Task DeleteAndroidIdSettingAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        return _adb.DeleteSettingAsync(
            serial,
            DeviceSettingsInfoConstants.SecureNamespace,
            DeviceSettingsInfoConstants.AndroidId,
            cancellationToken);
    }

    private async Task VerifyRegeneratedAndroidIdAsync(
        string serial,
        string originalAndroidId,
        CancellationToken cancellationToken)
    {
        string currentAndroidId = await ReadAndroidIdAsync(serial, cancellationToken).ConfigureAwait(false);

        if (!HasExistingAndroidId(currentAndroidId)
            || (HasExistingAndroidId(originalAndroidId)
                && string.Equals(currentAndroidId.Trim(), originalAndroidId.Trim(), StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Android ID was not regenerated on device {serial}.");
        }
    }

    private async Task VerifyAppliedProfileAsync(
        string serial,
        DeviceInfoApiDevice expected,
        CancellationToken cancellationToken)
    {
        var mismatches = new List<string>();
        await VerifyPropertyAsync(
            PropertyConstants.Spoof.ProductBrand,
            PropertyConstants.Runtime.ProductBrand,
            expected.Brand,
            mismatches);
        await VerifyPropertyAsync(
            PropertyConstants.Spoof.ProductManufacturer,
            PropertyConstants.Runtime.ProductManufacturer,
            expected.Manufacturer,
            mismatches);
        await VerifyPropertyAsync(
            PropertyConstants.Spoof.ProductModel,
            PropertyConstants.Runtime.ProductModel,
            expected.Model,
            mismatches);
        await VerifyPropertyAsync(
            PropertyConstants.Spoof.ProductDevice,
            PropertyConstants.Runtime.ProductDevice,
            expected.Code,
            mismatches);
        await VerifyPropertyAsync(
            PropertyConstants.Spoof.ProductName,
            PropertyConstants.Runtime.ProductName,
            expected.Name,
            mismatches);
        await VerifyPropertyAsync(
            PropertyConstants.Spoof.AndroidRelease,
            PropertyConstants.Runtime.AndroidRelease,
            expected.Release,
            mismatches);
        await VerifyPropertyAsync(
            PropertyConstants.Spoof.BuildFingerprint,
            PropertyConstants.Runtime.BuildFingerprint,
            expected.Fingerprint,
            mismatches);
        await VerifyPropertyAsync(
            PropertyConstants.Spoof.BuildId,
            PropertyConstants.Runtime.BuildId,
            expected.BuildId,
            mismatches);
        await VerifyPropertyAsync(
            PropertyConstants.Spoof.SecurityPatch,
            PropertyConstants.Runtime.SecurityPatch,
            expected.SecurityPatch,
            mismatches);

        string expectedDeviceName = FirstValue(
            expected.SettingDeviceName,
            expected.Name,
            expected.Model);
        string actualDeviceName = await _adb.GetSettingAsync(
                serial,
                DeviceSettingsInfoConstants.GlobalNamespace,
                DeviceSettingsInfoConstants.DeviceName,
                cancellationToken)
            .ConfigureAwait(false);
        AddMismatch(
            "global.device_name",
            expectedDeviceName,
            actualDeviceName,
            mismatches);

        string expectedBluetoothName = FirstValue(
            expected.SettingBluetoothName,
            expectedDeviceName);
        string actualBluetoothName = await _adb.GetSettingAsync(
                serial,
                DeviceSettingsInfoConstants.SecureNamespace,
                DeviceSettingsInfoConstants.BluetoothName,
                cancellationToken)
            .ConfigureAwait(false);
        AddMismatch(
            "secure.bluetooth_name",
            expectedBluetoothName,
            actualBluetoothName,
            mismatches);

        if (mismatches.Count > 0)
        {
            throw new InvalidOperationException(
                $"Device profile verification failed on {serial}: {string.Join(", ", mismatches)}.");
        }

        return;

        async Task VerifyPropertyAsync(
            string spoofProperty,
            string runtimeProperty,
            string? expectedValue,
            List<string> mismatchList)
        {
            if (string.IsNullOrWhiteSpace(expectedValue))
                return;

            string actualValue = await _adb
                .GetPropertyAsync(serial, runtimeProperty, cancellationToken)
                .ConfigureAwait(false);
            AddMismatch(spoofProperty, expectedValue, actualValue, mismatchList);
        }
    }

    private static void AddMismatch(
        string field,
        string? expected,
        string? actual,
        List<string> mismatches)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return;
        if (string.Equals(
                expected.Trim(),
                actual?.Trim(),
                StringComparison.Ordinal))
        {
            return;
        }

        mismatches.Add(field);
    }

    private static bool HasExistingAndroidId(string androidId)
    {
        string normalizedAndroidId = androidId.Trim();
        return normalizedAndroidId.Length > 0
            && !string.Equals(normalizedAndroidId, "null", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunRequiredShellAsync(
        string serial,
        string command,
        string purpose,
        CancellationToken cancellationToken)
    {
        CommandResult result = await _adb.RunAdbShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, serial, purpose);
    }

    private static void EnsureSuccess(CommandResult result, string serial, string purpose)
    {
        if (result.ExitCode == 0)
            return;

        throw new InvalidOperationException(
            $"ADB operation '{purpose}' failed on device {serial} with exit code {result.ExitCode}.");
    }

    private static void Validate(
        string serial,
        DeviceInfoApiDevice profile,
        DeviceChangeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(profile.Brand)
            || string.IsNullOrWhiteSpace(profile.Model)
            || string.IsNullOrWhiteSpace(profile.Fingerprint)
            || string.IsNullOrWhiteSpace(profile.Serial))
        {
            throw new InvalidOperationException("The generated device profile is incomplete.");
        }
    }

    private static void ValidateSimProfile(SimProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Iccid)
            || string.IsNullOrWhiteSpace(profile.Imsi)
            || string.IsNullOrWhiteSpace(profile.OperatorNumeric)
            || string.IsNullOrWhiteSpace(profile.OperatorCountry))
        {
            throw new InvalidOperationException("The generated SIM profile is incomplete.");
        }
    }

    private static KeyValuePair<string, string> Pair(string key, string? value)
    {
        return new KeyValuePair<string, string>(key, value?.Trim() ?? string.Empty);
    }

    private static string FirstValue(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
