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
    private readonly ILogger<DeviceChangeService> _logger;

    public DeviceChangeService(
        IAdbCommandService adb,
        IDeviceDataCleanupService cleanupService,
        ILogger<DeviceChangeService> logger)
    {
        _adb = adb;
        _cleanupService = cleanupService;
        _logger = logger;
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
        SemaphoreSlim deviceLock = _deviceLocks.GetOrAdd(serial, _ => new SemaphoreSlim(1, 1));
        await deviceLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            progress?.Report(DeviceChangeStage.Preparing);
            await EnsureRootAsync(serial, cancellationToken).ConfigureAwait(false);
            await _adb.SetWifiAsync(serial, false, cancellationToken).ConfigureAwait(false);

            string originalAndroidId = await _adb
                .GetSettingAsync(
                    serial,
                    DeviceChangeConstants.SecureSettingsNamespace,
                    DeviceChangeConstants.AndroidIdSetting,
                    cancellationToken)
                .ConfigureAwait(false);

            bool changeAndroidId = options.UseDefaultMode || options.ChangeAndroidId;
            string targetAndroidId = changeAndroidId ? profile.AndroidId : string.Empty;
            bool changeMacAddress = options.UseDefaultMode || options.ChangeMacAddress;

            progress?.Report(DeviceChangeStage.ClearingData);
            await _cleanupService.CleanAsync(serial, options, cancellationToken).ConfigureAwait(false);

            progress?.Report(DeviceChangeStage.ApplyingProfile);
            await ApplyProfileAsync(
                    serial,
                    profile,
                    targetAndroidId,
                    changeAndroidId,
                    changeSim,
                    changeMacAddress,
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(DeviceChangeStage.Rebooting);
            await _adb.RebootAsync(serial, cancellationToken).ConfigureAwait(false);

            progress?.Report(DeviceChangeStage.WaitingForDevice);
            await WaitForBootCompletedAsync(serial, cancellationToken).ConfigureAwait(false);

            progress?.Report(DeviceChangeStage.Verifying);
            await VerifyAndroidIdAsync(
                    serial,
                    originalAndroidId,
                    targetAndroidId,
                    changeAndroidId,
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(DeviceChangeStage.Completed);
            _logger.LogInformation(
                "Changed device identity and rebooted device {Serial}. Default mode: {DefaultMode}; Android ID changed: {AndroidIdChanged}; package cleanup: {PackageCleanup}; rm -rf package cleanup requested: {UseRmRfForPackageCleanup}.",
                serial,
                options.UseDefaultMode,
                changeAndroidId,
                DeviceChangeOptionsHelper.HasPackageCleanup(options),
                options.UseRmRfForPackageCleanup);
        }
        finally
        {
            deviceLock.Release();
        }
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
        string targetAndroidId,
        bool changeAndroidId,
        bool changeSim,
        bool changeMacAddress,
        CancellationToken cancellationToken)
    {
        await SetPropertiesAsync(
                serial,
                CreateIdentityProperties(profile, targetAndroidId, changeMacAddress),
                cancellationToken)
            .ConfigureAwait(false);
        await SetPropertiesAsync(serial, CreateSimProperties(profile, changeSim), cancellationToken)
            .ConfigureAwait(false);

        string deviceName = FirstValue(profile.SettingDeviceName, profile.Name, profile.Model);
        await _adb.PutSettingAsync(
                serial,
                DeviceChangeConstants.GlobalSettingsNamespace,
                DeviceChangeConstants.DeviceNameSetting,
                deviceName,
                cancellationToken)
            .ConfigureAwait(false);
        await ApplyAndroidIdSettingAsync(serial, targetAndroidId, changeAndroidId, cancellationToken)
            .ConfigureAwait(false);

        if (changeMacAddress)
        {
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

    private async Task ApplyAndroidIdSettingAsync(
        string serial,
        string targetAndroidId,
        bool changeAndroidId,
        CancellationToken cancellationToken)
    {
        if (changeAndroidId)
        {
            await _adb.PutSettingAsync(
                    serial,
                    DeviceChangeConstants.SecureSettingsNamespace,
                    DeviceChangeConstants.AndroidIdSetting,
                    targetAndroidId,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await _adb.DeleteSettingAsync(
                serial,
                DeviceChangeConstants.SecureSettingsNamespace,
                DeviceChangeConstants.AndroidIdSetting,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> CreateIdentityProperties(
        DeviceInfoApiDevice profile,
        string targetAndroidId,
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
            Pair(DeviceSpoofPropertyConstants.AndroidId, targetAndroidId),
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
        return
        [
            Pair(
                DeviceSpoofPropertyConstants.SimEnabled,
                changeSim ? DeviceChangeConstants.EnabledValue : DeviceChangeConstants.DisabledValue),
            Pair(DeviceSpoofPropertyConstants.SimIccid, changeSim ? profile.Iccid : string.Empty),
            Pair(DeviceSpoofPropertyConstants.SimImsi, changeSim ? profile.Imsi : string.Empty),
            Pair(DeviceSpoofPropertyConstants.SimPhoneNumber, changeSim ? profile.SimPhoneNumber : string.Empty),
            Pair(DeviceSpoofPropertyConstants.SimOperatorName, changeSim ? profile.SimOperatorName : string.Empty),
            Pair(DeviceSpoofPropertyConstants.SimOperatorCountry, changeSim ? profile.SimOperatorCountry : string.Empty),
            Pair(DeviceSpoofPropertyConstants.SimOperatorNumeric, changeSim ? profile.SimOperatorNumeric : string.Empty),
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

    private async Task VerifyAndroidIdAsync(
        string serial,
        string originalAndroidId,
        string targetAndroidId,
        bool changeAndroidId,
        CancellationToken cancellationToken)
    {
        string currentAndroidId = await _adb
            .GetSettingAsync(
                serial,
                DeviceChangeConstants.SecureSettingsNamespace,
                DeviceChangeConstants.AndroidIdSetting,
                cancellationToken)
            .ConfigureAwait(false);

        bool verificationFailed = changeAndroidId
            ? !string.Equals(currentAndroidId.Trim(), targetAndroidId.Trim(), StringComparison.Ordinal)
            : HasExistingAndroidId(originalAndroidId)
                && string.Equals(currentAndroidId.Trim(), originalAndroidId.Trim(), StringComparison.Ordinal);
        if (verificationFailed)
            throw new InvalidOperationException($"Android ID verification failed on device {serial}.");
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

    private static void Validate(string serial, DeviceInfoApiDevice profile, DeviceChangeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(profile.Brand)
            || string.IsNullOrWhiteSpace(profile.Model)
            || string.IsNullOrWhiteSpace(profile.Fingerprint)
            || string.IsNullOrWhiteSpace(profile.Serial)
            || (!options.UseDefaultMode
                && options.ChangeAndroidId
                && string.IsNullOrWhiteSpace(profile.AndroidId)))
        {
            throw new InvalidOperationException("The generated device profile is incomplete.");
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
