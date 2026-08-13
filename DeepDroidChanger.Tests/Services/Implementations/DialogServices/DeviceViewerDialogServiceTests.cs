using DeepDroidChanger.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeviceViewerDialogServiceTests
{
    [TestMethod]
    public async Task ShowDeviceViewerAsync_PreCancelled_DoesNotCreateScopeOrWindow()
    {
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        var service = new DeviceViewerDialogService(
            scopeFactory,
            NullLogger<DeviceViewerDialogService>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ShowDeviceViewerAsync(
            "SERIAL", "Device", cancellation.Token));

        scopeFactory.DidNotReceive().CreateScope();
    }
}
