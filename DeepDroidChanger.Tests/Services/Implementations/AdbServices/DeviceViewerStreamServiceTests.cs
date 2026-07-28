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
