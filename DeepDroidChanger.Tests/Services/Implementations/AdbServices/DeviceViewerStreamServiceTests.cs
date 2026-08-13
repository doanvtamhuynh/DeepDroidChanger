using DeepDroidChanger.Constants;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class DeviceViewerStreamServiceTests
{
    [TestMethod]
    public async Task ResolveToolPathAsync_UsesBundledViewScreenDirectory()
    {
        string expectedPath = Path.Combine(
            AppContext.BaseDirectory,
            AssetConstants.Tools.RootRelativePath,
            AssetConstants.Tools.ViewScreenDirectoryName,
            AssetConstants.Tools.ScrcpyExecutableName);

        string actualPath = await DeviceViewerStreamService.ResolveToolPathAsync(CancellationToken.None);

        Assert.AreEqual(expectedPath, actualPath);
    }

    [TestMethod]
    public void Service_ImplementsDeterministicApplicationShutdownCleanup()
    {
        Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(typeof(DeviceViewerStreamService)));
    }

    [TestMethod]
    public void CreateScrcpyStartInfo_UsesCanonicalAdbEnvironment()
    {
        var startInfo = DeviceViewerStreamService.CreateScrcpyStartInfo(
            "scrcpy.exe",
            "tools",
            "SERIAL",
            "TITLE",
            new Models.DeviceViewerStreamBounds(1, 2, 100, 200),
            "C:\\canonical\\platform-tools\\adb.exe");

        Assert.AreEqual(
            "C:\\canonical\\platform-tools\\adb.exe",
            startInfo.Environment["ADB"]);
    }

    [TestMethod]
    public async Task StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var service = new DeviceViewerStreamService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeviceViewerStreamService>.Instance);
        service.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => service.StartAsync(
                "serial",
                new IntPtr(1),
                new Models.DeviceViewerStreamBounds(0, 0, 100, 100),
                CancellationToken.None));
    }
}
