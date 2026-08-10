using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class DeviceTimezoneServiceTests
{
    [TestMethod]
    public async Task ApplyTimezoneAsync_WritesTimezoneAndBroadcastsChanges()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = new DeviceTimezoneService(
            adb,
            Substitute.For<IIpGeolocationService>(),
            NullLogger<DeviceTimezoneService>.Instance);

        await service.ApplyTimezoneAsync("SERIAL", "Asia/Ho_Chi_Minh", CancellationToken.None);

        await adb.Received(1).PutSettingAsync("SERIAL", "global", "auto_time_zone", "0", Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL", PropertyConstants.Timezone, "Asia/Ho_Chi_Minh", Arg.Any<CancellationToken>());
        await adb.Received(1).BroadcastAsync("SERIAL", "android.intent.action.TIMEZONE_CHANGED", Arg.Any<CancellationToken>());
        await adb.Received(1).BroadcastAsync("SERIAL", "android.intent.action.TIME_SET", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveTimezoneByDeviceIpAsync_ReturnsLookupTimezone()
    {
        IIpGeolocationService geolocation = Substitute.For<IIpGeolocationService>();
        geolocation.GetDeviceIpGeolocationAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new IpGeolocationInfo { Timezone = "Asia/Ho_Chi_Minh" });
        var service = new DeviceTimezoneService(
            Substitute.For<IAdbCommandService>(),
            geolocation,
            NullLogger<DeviceTimezoneService>.Instance);

        string timezone = await service.ResolveTimezoneByDeviceIpAsync("SERIAL", CancellationToken.None);

        Assert.AreEqual("Asia/Ho_Chi_Minh", timezone);
    }

    [TestMethod]
    public async Task ApplyAsync_DeviceIp_ReturnsTheResolvedTimezoneThatWasApplied()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        IIpGeolocationService geolocation = Substitute.For<IIpGeolocationService>();
        geolocation.GetDeviceIpGeolocationAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new IpGeolocationInfo { Timezone = "Asia/Ho_Chi_Minh" });
        var service = new DeviceTimezoneService(adb, geolocation, NullLogger<DeviceTimezoneService>.Instance);

        string timezone = await service.ApplyAsync(
            "SERIAL",
            new ChangeTimezoneDialogResult(ChangeTimezoneMode.DeviceIp, string.Empty),
            CancellationToken.None);

        Assert.AreEqual("Asia/Ho_Chi_Minh", timezone);
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Timezone,
            "Asia/Ho_Chi_Minh",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveTimezoneByDeviceIpAsync_RejectsEmptyTimezone()
    {
        IIpGeolocationService geolocation = Substitute.For<IIpGeolocationService>();
        geolocation.GetDeviceIpGeolocationAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new IpGeolocationInfo { Timezone = "  " });
        var service = new DeviceTimezoneService(
            Substitute.For<IAdbCommandService>(),
            geolocation,
            NullLogger<DeviceTimezoneService>.Instance);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ResolveTimezoneByDeviceIpAsync("SERIAL", CancellationToken.None));
    }

    [TestMethod]
    public async Task ApplyTimezoneAsync_RejectsEmptyTimezoneBeforeAdbMutation()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = new DeviceTimezoneService(
            adb,
            Substitute.For<IIpGeolocationService>(),
            NullLogger<DeviceTimezoneService>.Instance);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.ApplyTimezoneAsync("SERIAL", " ", CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
    }
}
