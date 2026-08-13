using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeviceViewerNativeWindowSyncSchedulerTests
{
    [TestMethod]
    public async Task RequestsDuringActiveSync_AreCoalescedAndFinalRequestIsProcessed()
    {
        var firstSyncGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var scheduler = new DeviceViewerNativeWindowSyncScheduler(async () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                await firstSyncGate.Task;
        });

        var first = scheduler.RequestAsync();
        var second = scheduler.RequestAsync();
        _ = scheduler.RequestAsync();

        Assert.AreSame(first, second);
        Assert.AreEqual(1, calls);
        firstSyncGate.SetResult();
        await first;

        Assert.AreEqual(2, calls);
        scheduler.Close();
    }

    [TestMethod]
    public async Task RequestAtWorkerDetachBoundary_StartsANewWorkerAndIsNotLost()
    {
        var calls = 0;
        var detachCallbacks = 0;
        Task? boundaryRequest = null;
        DeviceViewerNativeWindowSyncScheduler? scheduler = null;
        scheduler = new DeviceViewerNativeWindowSyncScheduler(
            () =>
            {
                Interlocked.Increment(ref calls);
                return Task.CompletedTask;
            },
            () =>
            {
                if (Interlocked.Increment(ref detachCallbacks) == 1)
                    boundaryRequest = scheduler!.RequestAsync();
            });

        await scheduler.RequestAsync();
        Assert.IsNotNull(boundaryRequest);
        await boundaryRequest!;

        Assert.AreEqual(2, calls);
        scheduler.Close();
    }

    [TestMethod]
    public async Task Close_PreventsLaterProcessing()
    {
        var calls = 0;
        var scheduler = new DeviceViewerNativeWindowSyncScheduler(() =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });

        scheduler.Close();
        await scheduler.RequestAsync();

        Assert.AreEqual(0, calls);
    }
}
