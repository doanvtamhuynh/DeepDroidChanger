using DeepDroidChanger.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeviceViewerDialogServiceTests
{
    [TestMethod]
    public async Task StartAndSynchronizeStreamBeforeDeviceIpRefreshAsync_PrioritizesNativeStream()
    {
        var operations = new List<string>();
        var syncCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task operation = DeviceViewerDialogService.StartAndSynchronizeStreamBeforeDeviceIpRefreshAsync(
            () =>
            {
                operations.Add("stream");
                return Task.CompletedTask;
            },
            () =>
            {
                operations.Add("sync");
                return syncCompletion.Task;
            },
            () => operations.Add("ip"));

        CollectionAssert.AreEqual(new[] { "stream", "sync" }, operations);
        Assert.IsFalse(operation.IsCompleted);

        syncCompletion.SetResult();
        await operation;

        CollectionAssert.AreEqual(new[] { "stream", "sync", "ip" }, operations);
    }

    [TestMethod]
    public async Task ShowDeviceViewerAsync_PreCancelled_DoesNotCreateScopeOrWindow()
    {
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        var service = new DeviceViewerDialogService(
            scopeFactory,
            Substitute.For<IDeviceViewerCoordinatorService>(),
            NullLogger<DeviceViewerDialogService>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ShowDeviceViewerAsync(
            "SERIAL", "Device", cancellation.Token));

        scopeFactory.DidNotReceive().CreateScope();
    }
}
