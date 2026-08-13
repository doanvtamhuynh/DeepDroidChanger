using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class DeviceViewerCoordinatorServiceTests
{
    [TestMethod]
    public void ParseDeviceAspectRatio_PhysicalSizeOnly_UsesPhysicalSize()
    {
        var ratio = DeviceViewerCoordinatorService.ParseDeviceAspectRatio(
            "Physical size: 1080x2400");

        Assert.AreEqual(1080d / 2400d, ratio, 0.000001d);
    }

    [TestMethod]
    public void ParseDeviceAspectRatio_OverrideSize_UsesEffectiveSize()
    {
        var ratio = DeviceViewerCoordinatorService.ParseDeviceAspectRatio(
            "Physical size: 1080x2400\nOverride size: 1440x2560");

        Assert.AreEqual(1440d / 2560d, ratio, 0.000001d);
    }

    [TestMethod]
    public void ParseDeviceAspectRatio_PortraitAndLandscapeValues_ArePreserved()
    {
        Assert.AreEqual(
            1080d / 2400d,
            DeviceViewerCoordinatorService.ParseDeviceAspectRatio("Physical size: 1080x2400"),
            0.000001d);
        Assert.AreEqual(
            2400d / 1080d,
            DeviceViewerCoordinatorService.ParseDeviceAspectRatio("Physical size: 2400x1080"),
            0.000001d);
    }

    [TestMethod]
    public void ParseDeviceAspectRatio_MalformedOutput_UsesFallback()
    {
        var ratio = DeviceViewerCoordinatorService.ParseDeviceAspectRatio(
            "Physical size: invalid\nOverride size: 0x0");

        Assert.AreEqual(9d / 20d, ratio, 0.000001d);
    }

    [TestMethod]
    public async Task QueryDeviceAspectRatioAsync_UsesParsedOverrideSize()
    {
        var commandService = Substitute.For<IAdbCommandService>();
        commandService
            .RunAdbAsync("SERIAL", "shell wm size", CancellationToken.None)
            .Returns(new CommandResult(
                0,
                "Physical size: 1080x2400\nOverride size: 720x1280",
                string.Empty));
        var coordinator = new DeviceViewerCoordinatorService(
            commandService,
            NullLogger<DeviceViewerCoordinatorService>.Instance);

        var ratio = await coordinator.QueryDeviceAspectRatioAsync("SERIAL", CancellationToken.None);

        Assert.AreEqual(720d / 1280d, ratio, 0.000001d);
    }
}
