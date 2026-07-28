using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

public sealed partial class DeviceChangeServiceTests
{
    private const string RunApiEnvironmentVariable = "DEEPDROID_RUN_DEVICE_API_MATRIX";
    private const string RunDeviceEnvironmentVariable = "DEEPDROID_RUN_DEVICE_CHANGE_MATRIX";
    private const string UsernameEnvironmentVariable = "DEEPDROID_DEVICE_API_USERNAME";
    private const string PasswordEnvironmentVariable = "DEEPDROID_DEVICE_API_PASSWORD";
    private const string SerialEnvironmentVariable = "DEEPDROID_DEVICE_SERIAL";
    private const string StartIndexEnvironmentVariable = "DEEPDROID_DEVICE_MATRIX_START_INDEX";
    private const string MarkerFileName = "deepdroidchanger_matrix_marker";

    private static readonly (string Brand, string AndroidVersion)[] Matrix =
    [
        ("Google", "Android 13"),
        ("Google", "Android 14"),
        ("Google", "Android 15"),
        ("Samsung", "Android 13"),
        ("Samsung", "Android 14"),
        ("Samsung", "Android 15"),
        ("Xiaomi", "Android 13"),
        ("Xiaomi", "Android 14"),
        ("Xiaomi", "Android 15"),
        ("OnePlus", "Android 13"),
        ("OPPO", "Android 14"),
        ("vivo", "Android 14")
    ];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("DeviceIntegration")]
    [Timeout(300_000)]
    public async Task DeviceInfoApi_AllSupportedBrandVersions_ReturnValidatedProfiles()
    {
        RequireOptIn(RunApiEnvironmentVariable);
        AccountSession session = await AuthenticateAsync(CancellationToken.None);
        IDeviceRandomProfileService profiles = CreateProfileService();

        foreach ((string brand, string androidVersion) in Matrix)
        {
            DeviceInfoApiDevice profile = await profiles.CreateRandomProfileAsync(
                session,
                CreateRequest(brand, androidVersion),
                CancellationToken.None);

            Assert.AreEqual(
                ExpectedSdk(androidVersion),
                profile.Sdk,
                $"Server SDK mismatch for {brand} {androidVersion}.");
            Assert.AreEqual(
                ExpectedRelease(androidVersion),
                profile.Release,
                $"Server release mismatch for {brand} {androidVersion}.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(profile.Model));
            Assert.IsFalse(string.IsNullOrWhiteSpace(profile.Fingerprint));
            TestContext.WriteLine(
                "API PASS | {0} | {1} | model={2} | release={3} | sdk={4}",
                brand,
                androidVersion,
                profile.Model,
                profile.Release,
                profile.Sdk);
        }
    }

    [TestMethod]
    [TestCategory("DeviceIntegration")]
    [Timeout(10_800_000)]
    public async Task RootedDevice_AllSupportedBrandVersions_RunThreeWorkflows()
    {
        RequireOptIn(RunDeviceEnvironmentVariable);
        string serial = RequireEnvironmentVariable(SerialEnvironmentVariable);
        IDeviceRandomProfileService profiles = CreateProfileService();
        (IAdbCommandService adb, IDeviceChangeService change) = CreateDeviceServices();
        int startIndex = ReadStartIndex();

        await EnsureSingleRootedDeviceAsync(adb, serial, CancellationToken.None);
        string packageName = await GetMarkerPackageAsync(adb, serial, CancellationToken.None);
        PackageInstallSnapshot packageInstall = await ReadPackageInstallSnapshotAsync(
            adb,
            serial,
            packageName,
            CancellationToken.None);
        string physicalSdk = await adb.GetPropertyAsync(
            serial,
            "ro.build.version.sdk",
            CancellationToken.None);

        for (int index = startIndex; index < Matrix.Length; index++)
        {
            (string brand, string androidVersion) = Matrix[index];
            DeviceChangeOptions options = CreateOptions(index, packageName);
            AccountSession session = await AuthenticateAsync(CancellationToken.None);

            DeviceInfoApiDevice fullProfile = await profiles.CreateRandomProfileAsync(
                session,
                CreateRequest(brand, androidVersion),
                CancellationToken.None);
            await WriteMarkerAsync(adb, serial, packageName, "before-full", CancellationToken.None);
            await change.ChangeAsync(
                serial,
                fullProfile,
                changeSim: true,
                options,
                progress: null,
                CancellationToken.None);
            await AssertMarkerStateAsync(
                adb,
                serial,
                packageName,
                expectedToExist: !OptionsClearMarkerPackage(options, packageName),
                "full change",
                CancellationToken.None);
            await AssertProfileAppliedAsync(
                adb,
                serial,
                fullProfile,
                physicalSdk,
                CancellationToken.None);
            await AssertPackageInstallPreservedAsync(
                adb,
                serial,
                packageInstall,
                CancellationToken.None);

            DeviceInfoApiDevice noWipeProfile = await profiles.CreateRandomProfileAsync(
                session,
                CreateRequest(brand, androidVersion),
                CancellationToken.None);
            await WriteMarkerAsync(adb, serial, packageName, "before-change-without-wipe", CancellationToken.None);
            await change.ChangeWithoutWipeAsync(
                serial,
                noWipeProfile,
                changeSim: true,
                options,
                progress: null,
                CancellationToken.None);
            await AssertMarkerStateAsync(
                adb,
                serial,
                packageName,
                expectedToExist: true,
                "change without wipe",
                CancellationToken.None);
            DeviceIdentitySnapshot noWipeIdentity = await AssertProfileAppliedAsync(
                adb,
                serial,
                noWipeProfile,
                physicalSdk,
                CancellationToken.None);
            await AssertPackageInstallPreservedAsync(
                adb,
                serial,
                packageInstall,
                CancellationToken.None);

            await WriteMarkerAsync(adb, serial, packageName, "before-wipe-without-change", CancellationToken.None);
            await change.WipeWithoutChangeAsync(
                serial,
                options,
                progress: null,
                CancellationToken.None);
            await AssertMarkerStateAsync(
                adb,
                serial,
                packageName,
                expectedToExist: !OptionsClearMarkerPackage(options, packageName),
                "wipe without change",
                CancellationToken.None);
            DeviceIdentitySnapshot afterWipeIdentity = await ReadIdentitySnapshotAsync(
                adb,
                serial,
                CancellationToken.None);
            Assert.AreEqual(
                noWipeIdentity,
                afterWipeIdentity,
                $"Wipe without change altered identity for {brand} {androidVersion}.");
            await AssertPackageInstallPreservedAsync(
                adb,
                serial,
                packageInstall,
                CancellationToken.None);

            TestContext.WriteLine(
                "DEVICE PASS | {0} | {1} | option={2} | physicalSdk={3} | profileSdk={4}",
                brand,
                androidVersion,
                DescribeOptions(options),
                physicalSdk,
                noWipeProfile.Sdk);
        }
    }

    private static IDeviceRandomProfileService CreateProfileService()
    {
        IDeviceIntegrityService integrity = Substitute.For<IDeviceIntegrityService>();
        IRandomService random = new RandomService();
        return new DeviceRandomProfileService(
            new DeviceRandomApiService(NullLogger<DeviceRandomApiService>.Instance),
            integrity,
            random,
            new SimProfileService(random));
    }

    private static (IAdbCommandService Adb, IDeviceChangeService Change) CreateDeviceServices()
    {
        var processRunner = new ProcessRunnerService(NullLogger<ProcessRunnerService>.Instance);
        IAdbCommandService adb = new AdbCommandService(
            processRunner,
            NullLogger<AdbCommandService>.Instance);
        var packages = new DevicePackageService(adb);
        var cleanup = new DeviceDataCleanupService(
            packages,
            adb,
            NullLogger<DeviceDataCleanupService>.Instance);
        IDeviceIntegrityService integrity = Substitute.For<IDeviceIntegrityService>();
        IDeviceChangeService change = new DeviceChangeService(
            adb,
            cleanup,
            integrity,
            NullLogger<DeviceChangeService>.Instance);
        return (adb, change);
    }

    private static async Task<AccountSession> AuthenticateAsync(CancellationToken cancellationToken)
    {
        string username = RequireEnvironmentVariable(UsernameEnvironmentVariable);
        string password = RequireEnvironmentVariable(PasswordEnvironmentVariable);
        DeviceInfoApiOptions options = new();
        DeviceInfoApiOptionsHelper.ApplyDefaults(options);
        var authentication = new AccountAuthenticationService(
            Options.Create(options),
            NullLogger<AccountAuthenticationService>.Instance);
        AccountAuthenticationResult result = await authentication.AuthenticateAsync(
            new AccountLoginRequest
            {
                Username = username,
                Password = password,
                RememberAccount = false
            },
            cancellationToken);

        Assert.AreEqual(
            AccountAuthenticationStatus.Success,
            result.Status,
            "Cognito authentication failed.");
        return result.Session
            ?? throw new AssertFailedException("Authentication returned no Device Info API session.");
    }

    private static async Task EnsureSingleRootedDeviceAsync(
        IAdbCommandService adb,
        string expectedSerial,
        CancellationToken cancellationToken)
    {
        CommandResult devicesResult = await adb.RunAdbAsync("devices", cancellationToken);
        Assert.AreEqual(0, devicesResult.ExitCode, "Unable to list ADB devices.");
        string[] onlineSerials = devicesResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.EndsWith("\tdevice", StringComparison.Ordinal))
            .Select(line => line.Split('\t', 2)[0])
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { expectedSerial },
            onlineSerials,
            "Destructive matrix requires exactly the explicitly selected online device.");

        CommandResult rootResult = await adb.RunAdbAsync(
            expectedSerial,
            "root",
            cancellationToken);
        Assert.AreEqual(0, rootResult.ExitCode, "Unable to restart adbd as root.");
        CommandResult waitResult = await adb.RunAdbAsync(
            expectedSerial,
            "wait-for-device",
            cancellationToken);
        Assert.AreEqual(0, waitResult.ExitCode, "Rooted device did not reconnect.");
        CommandResult identityResult = await adb.RunAdbShellAsync(
            expectedSerial,
            "id -u",
            cancellationToken);
        Assert.AreEqual("0", identityResult.StandardOutput.Trim(), "ADB session is not root.");
    }

    private static async Task<string> GetMarkerPackageAsync(
        IAdbCommandService adb,
        string serial,
        CancellationToken cancellationToken)
    {
        var packages = new DevicePackageService(adb);
        string packageName = (await packages
                .GetUserInstalledPackagesAsync(serial, cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault()
            ?? string.Empty;
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(packageName),
            "At least one user-installed package is required for package-data verification.");
        return packageName;
    }

    private static async Task<PackageInstallSnapshot> ReadPackageInstallSnapshotAsync(
        IAdbCommandService adb,
        string serial,
        string packageName,
        CancellationToken cancellationToken)
    {
        CommandResult path = await adb.RunAdbShellAsync(
            serial,
            $"pm path \"{packageName}\"",
            cancellationToken);
        CommandResult version = await adb.RunAdbShellAsync(
            serial,
            $"dumpsys package \"{packageName}\" | grep -m 1 'versionCode='",
            cancellationToken);
        Assert.AreEqual(0, path.ExitCode, $"Unable to read APK path for {packageName}.");
        Assert.AreEqual(0, version.ExitCode, $"Unable to read version for {packageName}.");
        Assert.Contains("package:", path.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("versionCode=", version.StandardOutput, StringComparison.Ordinal);
        return new PackageInstallSnapshot(
            packageName,
            path.StandardOutput.Trim(),
            version.StandardOutput.Trim());
    }

    private static async Task AssertPackageInstallPreservedAsync(
        IAdbCommandService adb,
        string serial,
        PackageInstallSnapshot expected,
        CancellationToken cancellationToken)
    {
        PackageInstallSnapshot actual = await ReadPackageInstallSnapshotAsync(
            adb,
            serial,
            expected.PackageName,
            cancellationToken);
        Assert.AreEqual(expected, actual, $"Installed APK/update changed for {expected.PackageName}.");
    }

    private static async Task WriteMarkerAsync(
        IAdbCommandService adb,
        string serial,
        string packageName,
        string value,
        CancellationToken cancellationToken)
    {
        string directory = $"/data/user/0/{packageName}/files";
        CommandResult result = await adb.RunAdbShellAsync(
            serial,
            $"mkdir -p \"{directory}\" && printf '%s' '{value}' > \"{directory}/{MarkerFileName}\" && sync",
            cancellationToken);
        Assert.AreEqual(0, result.ExitCode, $"Unable to write package-data marker for {packageName}.");
    }

    private static async Task AssertMarkerStateAsync(
        IAdbCommandService adb,
        string serial,
        string packageName,
        bool expectedToExist,
        string workflow,
        CancellationToken cancellationToken)
    {
        CommandResult result = await adb.RunAdbShellAsync(
            serial,
            $"test -f \"/data/user/0/{packageName}/files/{MarkerFileName}\"",
            cancellationToken);
        Assert.AreEqual(
            expectedToExist,
            result.ExitCode == 0,
            $"Package marker state is wrong after {workflow} for {packageName}.");
    }

    private static async Task<DeviceIdentitySnapshot> AssertProfileAppliedAsync(
        IAdbCommandService adb,
        string serial,
        DeviceInfoApiDevice expected,
        string physicalSdk,
        CancellationToken cancellationToken)
    {
        DeviceIdentitySnapshot actual = await ReadIdentitySnapshotAsync(
            adb,
            serial,
            cancellationToken);
        Assert.AreEqual(expected.Brand, actual.Brand, "Brand was not applied.");
        Assert.AreEqual(expected.Manufacturer, actual.Manufacturer, "Manufacturer was not applied.");
        Assert.AreEqual(expected.Model, actual.Model, "Model was not applied.");
        Assert.AreEqual(expected.Code, actual.Device, "Device code was not applied.");
        Assert.AreEqual(expected.Name, actual.ProductName, "Product name was not applied.");
        Assert.AreEqual(expected.Release, actual.Release, "Android release was not applied.");
        Assert.AreEqual(expected.Fingerprint, actual.Fingerprint, "Fingerprint was not applied.");
        Assert.AreEqual(expected.BuildId, actual.BuildId, "Build ID was not applied.");
        Assert.AreEqual(expected.SecurityPatch, actual.SecurityPatch, "Security patch was not applied.");
        Assert.AreEqual(expected.SettingDeviceName, actual.DeviceName, "Global device name was not applied.");
        Assert.AreEqual(expected.SettingBluetoothName, actual.BluetoothName, "Bluetooth name was not applied.");
        Assert.AreEqual(
            physicalSdk,
            actual.RuntimeSdk,
            "Physical PixelExperience Android 13 SDK unexpectedly changed.");
        return actual;
    }

    private static async Task<DeviceIdentitySnapshot> ReadIdentitySnapshotAsync(
        IAdbCommandService adb,
        string serial,
        CancellationToken cancellationToken)
    {
        return new DeviceIdentitySnapshot(
            await adb.GetPropertyAsync(serial, "ro.product.brand", cancellationToken),
            await adb.GetPropertyAsync(serial, "ro.product.manufacturer", cancellationToken),
            await adb.GetPropertyAsync(serial, "ro.product.model", cancellationToken),
            await adb.GetPropertyAsync(serial, "ro.product.device", cancellationToken),
            await adb.GetPropertyAsync(serial, "ro.product.name", cancellationToken),
            await adb.GetPropertyAsync(serial, "ro.build.version.release", cancellationToken),
            await adb.GetPropertyAsync(serial, "ro.build.version.sdk", cancellationToken),
            await adb.GetPropertyAsync(serial, "ro.build.fingerprint", cancellationToken),
            await adb.GetPropertyAsync(serial, "ro.build.id", cancellationToken),
            await adb.GetPropertyAsync(serial, "ro.build.version.security_patch", cancellationToken),
            await adb.GetSettingAsync(
                serial,
                DeviceSettingsInfoConstants.GlobalNamespace,
                DeviceSettingsInfoConstants.DeviceName,
                cancellationToken),
            await adb.GetSettingAsync(
                serial,
                DeviceSettingsInfoConstants.SecureNamespace,
                DeviceSettingsInfoConstants.BluetoothName,
                cancellationToken));
    }

    private static RandomDeviceRequest CreateRequest(string brand, string androidVersion)
    {
        return new RandomDeviceRequest
        {
            SelectedBrand = brand,
            SelectedAndroidVersion = androidVersion,
            UseIntegritySecurityPatch = false
        };
    }

    private static DeviceChangeOptions CreateOptions(int index, string markerPackage)
    {
        return (index % 4) switch
        {
            0 => new DeviceChangeOptions
            {
                UseDefaultMode = true
            },
            1 => new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeMacAddress = true,
                ClearAllPackages = false,
                ClearSelectedPackages = true,
                ClearGooglePackages = false,
                ClearGoogleAccounts = false,
                SelectedPackages = [markerPackage]
            },
            2 => new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeMacAddress = true,
                ClearAllPackages = false,
                ClearSelectedPackages = false,
                ClearGooglePackages = true,
                ClearGoogleAccounts = true
            },
            _ => new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeMacAddress = true,
                UseRmRfForPackageCleanup = true,
                ClearAllPackages = true,
                ClearGoogleAccounts = true
            }
        };
    }

    private static bool OptionsClearMarkerPackage(
        DeviceChangeOptions options,
        string markerPackage)
    {
        return options.UseDefaultMode
            || options.ClearAllPackages
            || (options.ClearSelectedPackages
                && options.SelectedPackages.Contains(markerPackage, StringComparer.Ordinal))
            || (options.ClearGooglePackages
                && DeviceDataCleanupService.IsGooglePackage(markerPackage));
    }

    private static string DescribeOptions(DeviceChangeOptions options)
    {
        if (options.UseDefaultMode)
            return "default-all";
        if (options.ClearAllPackages)
            return options.UseRmRfForPackageCleanup ? "advanced-all-deep" : "advanced-all";
        if (options.ClearGooglePackages || options.ClearGoogleAccounts)
            return "advanced-google-account";
        return "advanced-selected";
    }

    private static string ExpectedSdk(string androidVersion)
    {
        return androidVersion switch
        {
            "Android 13" => "33",
            "Android 14" => "34",
            "Android 15" => "35",
            _ => throw new AssertFailedException($"Unsupported Android version: {androidVersion}")
        };
    }

    private static string ExpectedRelease(string androidVersion)
    {
        return androidVersion switch
        {
            "Android 13" => "13",
            "Android 14" => "14",
            "Android 15" => "15",
            _ => throw new AssertFailedException($"Unsupported Android version: {androidVersion}")
        };
    }

    private static void RequireOptIn(string environmentVariable)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(environmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {environmentVariable}=1 to run this integration test.");
        }
    }

    private static int ReadStartIndex()
    {
        string? value = Environment.GetEnvironmentVariable(StartIndexEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        Assert.IsTrue(
            int.TryParse(value, out int startIndex)
            && startIndex >= 0
            && startIndex < Matrix.Length,
            $"{StartIndexEnvironmentVariable} must be between 0 and {Matrix.Length - 1}.");
        return startIndex;
    }

    private static string RequireEnvironmentVariable(string name)
    {
        string value = Environment.GetEnvironmentVariable(name)?.Trim() ?? string.Empty;
        Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"Environment variable {name} is required.");
        return value;
    }

    private sealed record PackageInstallSnapshot(
        string PackageName,
        string ApkPath,
        string Version);

    private sealed record DeviceIdentitySnapshot(
        string Brand,
        string Manufacturer,
        string Model,
        string Device,
        string ProductName,
        string Release,
        string RuntimeSdk,
        string Fingerprint,
        string BuildId,
        string SecurityPatch,
        string DeviceName,
        string BluetoothName);
}
