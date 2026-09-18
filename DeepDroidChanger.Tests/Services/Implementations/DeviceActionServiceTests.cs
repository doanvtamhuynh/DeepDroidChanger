using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class DeviceActionServiceTests
{
    [TestMethod]
    public async Task GetGooglePackageStateAsync_MapsExactDisabledPackageNames()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(
            [
                "com.google.android.gms",
                "com.android.vending"
            ]);
        packages.GetDisabledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(
            [
                "com.google.android.gms",
                "com.android.vending.extra"
            ]);
        var service = new DeviceActionService(adb, packages);

        GooglePackageState state =
            await service.GetGooglePackageStateAsync("SERIAL", CancellationToken.None);

        Assert.IsTrue(state.IsGmsDisabled);
        Assert.IsFalse(state.IsPlayStoreDisabled);
        Assert.IsTrue(state.IsGmsInstalled);
        Assert.IsTrue(state.IsPlayStoreInstalled);
    }

    [TestMethod]
    public async Task GetGooglePackageStateAsync_MissingGmsIsNotReportedEnabled()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(["com.android.vending"]);
        packages.GetDisabledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns([]);
        var service = new DeviceActionService(
            Substitute.For<IAdbCommandService>(),
            packages);

        GooglePackageState state =
            await service.GetGooglePackageStateAsync("SERIAL", CancellationToken.None);

        Assert.IsFalse(state.IsGmsInstalled);
        Assert.IsFalse(state.IsGmsDisabled);
        Assert.IsTrue(state.IsPlayStoreInstalled);
    }

    [TestMethod]
    public async Task GetGooglePackageStateAsync_MissingPlayStoreIsNotReportedEnabled()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(["com.google.android.gms"]);
        packages.GetDisabledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns([]);
        var service = new DeviceActionService(
            Substitute.For<IAdbCommandService>(),
            packages);

        GooglePackageState state =
            await service.GetGooglePackageStateAsync("SERIAL", CancellationToken.None);

        Assert.IsTrue(state.IsGmsInstalled);
        Assert.IsFalse(state.IsGmsDisabled);
        Assert.IsFalse(state.IsPlayStoreInstalled);
        Assert.IsFalse(state.IsPlayStoreDisabled);
    }

    [TestMethod]
    public async Task SetGmsEnabledAsync_TargetsGoogleMobileServicesPackage()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        var service = new DeviceActionService(adb, packages);

        await service.SetGmsEnabledAsync("SERIAL", enabled: true, CancellationToken.None);

        await packages.Received(1).SetPackageEnabledAsync(
            "SERIAL",
            "com.google.android.gms",
            true,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task SetPlayStoreEnabledAsync_TargetsPlayStorePackage()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        var service = new DeviceActionService(adb, packages);

        await service.SetPlayStoreEnabledAsync("SERIAL", enabled: false, CancellationToken.None);

        await packages.Received(1).SetPackageEnabledAsync(
            "SERIAL",
            "com.android.vending",
            false,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("1\r\n", true)]
    [DataRow("0\r\n", false)]
    public async Task GetWifiEnabledAsync_MapsOnlyGlobalSettingOneToEnabled(
        string settingValue,
        bool expectedEnabled)
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.GetSettingAsync("SERIAL", "global", "wifi_on", Arg.Any<CancellationToken>())
            .Returns(settingValue);
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        var service = new DeviceActionService(adb, packages);

        bool enabled = await service.GetWifiEnabledAsync("SERIAL", CancellationToken.None);

        Assert.AreEqual(expectedEnabled, enabled);
        await adb.Received(1).GetSettingAsync(
            "SERIAL",
            "global",
            "wifi_on",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetWifiEnabledAsync_UnknownValueThrows()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.GetSettingAsync("SERIAL", "global", "wifi_on", Arg.Any<CancellationToken>())
            .Returns("unknown\r\n");
        var service = new DeviceActionService(adb, Substitute.For<IDevicePackageService>());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.GetWifiEnabledAsync("SERIAL", CancellationToken.None));
    }

    [TestMethod]
    [DataRow("mInteractive=true", true)]
    [DataRow("mInteractive=false", false)]
    [DataRow("mWakefulness=Awake", true)]
    [DataRow("mWakefulness=Asleep", false)]
    [DataRow("Display Power: state=ON", true)]
    [DataRow("Display Power: state=OFF", false)]
    public async Task GetScreenOnAsync_ParsesSupportedPowerStates(
        string output,
        bool expectedOn)
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", "dumpsys power", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, output, string.Empty));
        var service = new DeviceActionService(adb, Substitute.For<IDevicePackageService>());

        bool? actual = await service.GetScreenOnAsync("SERIAL", CancellationToken.None);

        Assert.AreEqual(expectedOn, actual);
    }

    [TestMethod]
    public async Task GetScreenOnAsync_UnknownOrFailedOutputReturnsNull()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", "dumpsys power", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "mWakefulness=Unknown", string.Empty));
        var service = new DeviceActionService(adb, Substitute.For<IDevicePackageService>());

        Assert.IsNull(await service.GetScreenOnAsync("SERIAL", CancellationToken.None));

        adb.RunAdbShellAsync("SERIAL", "dumpsys power", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "failed"));
        Assert.IsNull(await service.GetScreenOnAsync("SERIAL", CancellationToken.None));
    }

    [TestMethod]
    public async Task GetScreenOnAsync_DoesNotTreatUnknownDisplayStateAsOn()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", "dumpsys power", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Display Power: state=UNKNOWN", string.Empty));
        var service = new DeviceActionService(adb, Substitute.For<IDevicePackageService>());

        Assert.IsNull(await service.GetScreenOnAsync("SERIAL", CancellationToken.None));
    }

    [TestMethod]
    public async Task SetWifiEnabledAsync_DelegatesRequestedStateToAdb()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        var service = new DeviceActionService(adb, packages);

        await service.SetWifiEnabledAsync("SERIAL", enabled: false, CancellationToken.None);

        await adb.Received(1).SetWifiAsync(
            "SERIAL",
            false,
            Arg.Any<CancellationToken>());
    }

}
