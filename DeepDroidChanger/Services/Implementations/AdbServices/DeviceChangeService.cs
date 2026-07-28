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
                    DeviceChangeConstants.SyncCommand,
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
                        DeviceChangeConstants.DisableBluetoothCommand,
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
                        DeviceChangeConstants.DisableBluetoothCommand,
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
            .RunAdbAsync(serial, AdbToolConstants.AdbRootCommand, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(rootResult, serial, "restart adbd as root");

        CommandResult waitResult = await _adb
            .RunAdbAsync(serial, AdbToolConstants.AdbWaitForDeviceCommand, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(waitResult, serial, "wait for rooted device");

        CommandResult identityResult = await _adb
            .RunAdbShellAsync(serial, DeviceChangeConstants.RootIdentityCommand, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(identityResult, serial, "verify root access");
        if (!string.Equals(
                identityResult.StandardOutput.Trim(),
                DeviceChangeConstants.RootUserId,
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
                DeviceChangeConstants.GlobalSettingsNamespace,
                DeviceChangeConstants.DeviceNameSetting,
                deviceName,
                cancellationToken)
            .ConfigureAwait(false);
        await _adb.PutSettingAsync(
                serial,
                DeviceChangeConstants.SecureSettingsNamespace,
                DeviceChangeConstants.BluetoothNameSetting,
                bluetoothName,
                cancellationToken)
            .ConfigureAwait(false);
        await _adb.PutSettingAsync(
                serial,
                DeviceChangeConstants.GlobalSettingsNamespace,
                DeviceChangeConstants.WifiP2pDeviceNameSetting,
                deviceName,
                cancellationToken)
            .ConfigureAwait(false);
        if (changeMacAddress)
        {
            await _adb.DeleteSettingAsync(
                    serial,
                    DeviceChangeConstants.SecureSettingsNamespace,
                    DeviceChangeConstants.BluetoothAddressSetting,
                    cancellationToken)
                .ConfigureAwait(false);
            await _adb.DeleteSettingAsync(
                    serial,
                    DeviceChangeConstants.SecureSettingsNamespace,
                    DeviceChangeConstants.BluetoothAddressValidSetting,
                    cancellationToken)
                .ConfigureAwait(false);
            await _adb.PutSettingAsync(
                    serial,
                    DeviceChangeConstants.GlobalSettingsNamespace,
                    DeviceChangeConstants.RandomMacSetting,
                    DeviceChangeConstants.EnabledValue,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _adb.PutSettingAsync(
                serial,
                DeviceChangeConstants.SystemSettingsNamespace,
                DeviceChangeConstants.ScreenTimeoutSetting,
                DeviceChangeConstants.ScreenTimeoutValue,
                cancellationToken)
            .ConfigureAwait(false);
        await RunRequiredShellAsync(
                serial,
                DeviceChangeConstants.DisableLockScreenCommand,
                "disable lock screen",
                cancellationToken)
            .ConfigureAwait(false);
        await RunRequiredShellAsync(
                serial,
                DeviceChangeConstants.SyncCommand,
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
            Pair(DeviceSpoofPropertyConstants.ProductBrand, profile.Brand),
            Pair(DeviceSpoofPropertyConstants.ProductDevice, profile.Code),
            Pair(DeviceSpoofPropertyConstants.ProductManufacturer, profile.Manufacturer),
            Pair(DeviceSpoofPropertyConstants.ProductModel, profile.Model),
            Pair(DeviceSpoofPropertyConstants.ProductName, profile.Name),
            Pair(DeviceSpoofPropertyConstants.BuildFingerprint, profile.Fingerprint),
            Pair(DeviceSpoofPropertyConstants.BuildId, profile.BuildId),
            Pair(DeviceSpoofPropertyConstants.BuildIncremental, profile.BuildIncremental),
            Pair(DeviceSpoofPropertyConstants.BuildDate, profile.BuildDate),
            Pair(DeviceSpoofPropertyConstants.BuildDateUtc, profile.BuildDateUtc),
            Pair(DeviceSpoofPropertyConstants.BuildUser, profile.BuildUser),
            Pair(DeviceSpoofPropertyConstants.BuildHost, profile.BuildHost),
            Pair(DeviceSpoofPropertyConstants.BuildFlavor, profile.BuildFlavor),
            Pair(DeviceSpoofPropertyConstants.BuildProduct, profile.Product),
            Pair(DeviceSpoofPropertyConstants.Hardware, profile.Hardware),
            Pair(DeviceSpoofPropertyConstants.Board, profile.Board),
            Pair(DeviceSpoofPropertyConstants.Platform, profile.Platform),
            Pair(DeviceSpoofPropertyConstants.Bootloader, profile.Bootloader),
            Pair(DeviceSpoofPropertyConstants.SocManufacturer, profile.Manufacturer),
            Pair(DeviceSpoofPropertyConstants.SocModel, profile.Hardware),
            Pair(DeviceSpoofPropertyConstants.SecurityPatch, profile.SecurityPatch),
            Pair(DeviceSpoofPropertyConstants.AndroidRelease, profile.Release),
            Pair(DeviceSpoofPropertyConstants.BuildDisplayId, profile.BuildDisplayId),
            Pair(DeviceSpoofPropertyConstants.BuildDescription, profile.BuildDescription),
            Pair(DeviceSpoofPropertyConstants.ClientIdBase, string.Concat("android-", profile.Brand)),
            Pair(DeviceSpoofPropertyConstants.Baseband, profile.Baseband),
            Pair(DeviceSpoofPropertyConstants.SerialNumber, profile.Serial),
            Pair(DeviceSpoofPropertyConstants.DeviceName, FirstValue(profile.SettingDeviceName, profile.Name, profile.Model)),
            Pair(DeviceSpoofPropertyConstants.VbmetaDigest, profile.VbmetaDigest),
            Pair(DeviceSpoofPropertyConstants.Imei0, profile.Imei),
            Pair(DeviceSpoofPropertyConstants.Imei1, profile.Imei1),
            Pair(DeviceSpoofPropertyConstants.BluetoothName, profile.SettingBluetoothName),
            Pair(DeviceSpoofPropertyConstants.WifiSsid, profile.WifiSsid)
        ];

        if (changeMacAddress)
        {
            properties.Add(Pair(DeviceSpoofPropertyConstants.BluetoothMac, profile.BluetoothMacAddress));
            properties.Add(Pair(DeviceSpoofPropertyConstants.WifiMac, profile.WifiMacAddress));
            properties.Add(Pair(DeviceSpoofPropertyConstants.WifiBssid, profile.WifiBssid));
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
                DeviceSpoofPropertyConstants.SimEnabled,
                enabled ? DeviceChangeConstants.EnabledValue : DeviceChangeConstants.DisabledValue),
            Pair(DeviceSpoofPropertyConstants.SimIccid, iccid),
            Pair(DeviceSpoofPropertyConstants.SimImsi, imsi),
            Pair(DeviceSpoofPropertyConstants.SimPhoneNumber, phoneNumber),
            Pair(DeviceSpoofPropertyConstants.SimOperatorName, operatorName),
            Pair(DeviceSpoofPropertyConstants.SimOperatorCountry, operatorCountry),
            Pair(DeviceSpoofPropertyConstants.SimOperatorNumeric, operatorNumeric),
            Pair(DeviceSpoofPropertyConstants.Sim2Enabled, DeviceChangeConstants.DisabledValue),
            Pair(DeviceSpoofPropertyConstants.Sim2Iccid, string.Empty),
            Pair(DeviceSpoofPropertyConstants.Sim2Imsi, string.Empty),
            Pair(DeviceSpoofPropertyConstants.Sim2PhoneNumber, string.Empty)
        ];
    }

    private async Task WaitForBootCompletedAsync(string serial, CancellationToken cancellationToken)
    {
        CommandResult waitResult = await _adb
            .RunAdbAsync(serial, AdbToolConstants.AdbWaitForDeviceCommand, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(waitResult, serial, "wait for rebooted device");

        for (int attempt = 0; attempt < DeviceChangeConstants.BootCompletionPollAttempts; attempt++)
        {
            CommandResult bootResult = await _adb
                .RunAdbShellAsync(serial, DeviceChangeConstants.BootCompletedCommand, cancellationToken)
                .ConfigureAwait(false);
            if (bootResult.ExitCode == 0
                && string.Equals(
                    bootResult.StandardOutput.Trim(),
                    DeviceChangeConstants.BootCompletedValue,
                    StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(DeviceChangeConstants.BootCompletionPollDelayMilliseconds, cancellationToken).ConfigureAwait(false);
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
                await _locationService.ApplyLocationAsync(
                        serial,
                        selectedLocation.LatitudeString,
                        selectedLocation.LongitudeString,
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
            DeviceChangeConstants.SecureSettingsNamespace,
            DeviceChangeConstants.AndroidIdSetting,
            cancellationToken);
    }

    private Task DeleteAndroidIdSettingAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        return _adb.DeleteSettingAsync(
            serial,
            DeviceChangeConstants.SecureSettingsNamespace,
            DeviceChangeConstants.AndroidIdSetting,
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
            DeviceSpoofPropertyConstants.ProductBrand,
            "ro.product.brand",
            expected.Brand,
            mismatches);
        await VerifyPropertyAsync(
            DeviceSpoofPropertyConstants.ProductManufacturer,
            "ro.product.manufacturer",
            expected.Manufacturer,
            mismatches);
        await VerifyPropertyAsync(
            DeviceSpoofPropertyConstants.ProductModel,
            "ro.product.model",
            expected.Model,
            mismatches);
        await VerifyPropertyAsync(
            DeviceSpoofPropertyConstants.ProductDevice,
            "ro.product.device",
            expected.Code,
            mismatches);
        await VerifyPropertyAsync(
            DeviceSpoofPropertyConstants.ProductName,
            "ro.product.name",
            expected.Name,
            mismatches);
        await VerifyPropertyAsync(
            DeviceSpoofPropertyConstants.AndroidRelease,
            "ro.build.version.release",
            expected.Release,
            mismatches);
        await VerifyPropertyAsync(
            DeviceSpoofPropertyConstants.BuildFingerprint,
            "ro.build.fingerprint",
            expected.Fingerprint,
            mismatches);
        await VerifyPropertyAsync(
            DeviceSpoofPropertyConstants.BuildId,
            "ro.build.id",
            expected.BuildId,
            mismatches);
        await VerifyPropertyAsync(
            DeviceSpoofPropertyConstants.SecurityPatch,
            "ro.build.version.security_patch",
            expected.SecurityPatch,
            mismatches);

        string expectedDeviceName = FirstValue(
            expected.SettingDeviceName,
            expected.Name,
            expected.Model);
        string actualDeviceName = await _adb.GetSettingAsync(
                serial,
                DeviceChangeConstants.GlobalSettingsNamespace,
                DeviceChangeConstants.DeviceNameSetting,
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
                DeviceChangeConstants.SecureSettingsNamespace,
                DeviceChangeConstants.BluetoothNameSetting,
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
