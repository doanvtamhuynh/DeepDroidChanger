using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class AdbDeviceServiceTests
{
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
}
