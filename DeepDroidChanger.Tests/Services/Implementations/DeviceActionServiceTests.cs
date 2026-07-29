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
    [DataRow("null\r\n", false)]
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
