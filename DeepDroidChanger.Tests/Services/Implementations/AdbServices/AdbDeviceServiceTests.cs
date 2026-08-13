using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class AdbDeviceServiceTests
{
    [TestMethod]
    public async Task IsDeviceOnlineAsync_UsesExplicitSerialGetState()
    {
        IAdbCommandService adbCommand = Substitute.For<IAdbCommandService>();
        adbCommand.RunAdbAsync("SERIAL-A", "get-state", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "device\r\n", string.Empty));
        var service = new AdbDeviceService(adbCommand);

        bool isOnline = await service.IsDeviceOnlineAsync("SERIAL-A", CancellationToken.None);

        Assert.IsTrue(isOnline);
        await adbCommand.Received(1).RunAdbAsync(
            "SERIAL-A",
            "get-state",
            CancellationToken.None);
    }

    [TestMethod]
    public void ParseDevices_DaemonNoiseAndKnownStates_ReturnsDeviceRowsOnly()
    {
        const string output = "* daemon started successfully *\n"
            + "List of devices attached\n"
            + "SERIAL-1\tdevice product:sargo\n"
            + "SERIAL-2\toffline\n"
            + "SERIAL-3\tunauthorized usb:1-2\n"
            + "noise-without-status\n";

        IReadOnlyList<AdbDevice> devices = AdbDeviceService.ParseDevices(output);

        Assert.HasCount(3, devices);
        Assert.AreEqual(AdbDeviceStatus.Online, devices[0].Status);
        Assert.AreEqual(AdbDeviceStatus.Offline, devices[1].Status);
        Assert.AreEqual(AdbDeviceStatus.Unauthorized, devices[2].Status);
    }

    [TestMethod]
    public void ParseTrackedDevices_MapsOnlineOfflineUnauthorizedAndDisappearancePayload()
    {
        IReadOnlyList<AdbDevice> devices = AdbDeviceService.ParseTrackedDevices(
            "SERIAL-1\tdevice\nSERIAL-2\toffline\nSERIAL-3\tunauthorized\n");

        Assert.HasCount(3, devices);
        Assert.AreEqual("SERIAL-1", devices[0].Serial);
        Assert.AreEqual(AdbDeviceStatus.Online, devices[0].Status);
        Assert.AreEqual(AdbDeviceStatus.Offline, devices[1].Status);
        Assert.AreEqual(AdbDeviceStatus.Unauthorized, devices[2].Status);
        Assert.IsEmpty(AdbDeviceService.ParseTrackedDevices(string.Empty));
    }
}
