using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ViewDevicePresentationCoordinatorTests
{
    [TestMethod]
    public async Task DedicatedLease_IsCaseInsensitive()
    {
        ViewDevicePresentationCoordinator coordinator = new();
        IAsyncDisposable lease = await coordinator.AcquireDedicatedViewAsync("SERIAL");

        Assert.IsTrue(coordinator.IsDedicatedViewActive("serial"));

        await lease.DisposeAsync();

        Assert.IsFalse(coordinator.IsDedicatedViewActive("SERIAL"));
    }

    [TestMethod]
    public async Task Acquire_WaitsForMultiViewSuspension()
    {
        ViewDevicePresentationCoordinator coordinator = new();
        TaskCompletionSource suspendStarted = NewSignal();
        TaskCompletionSource allowSuspend = NewSignal();
        using IDisposable registration = coordinator.RegisterMultiView(
            async (_, cancellationToken) =>
            {
                suspendStarted.TrySetResult();
                await allowSuspend.Task.WaitAsync(cancellationToken);
            },
            (_, _) => Task.CompletedTask);

        Task<IAsyncDisposable> acquire = coordinator.AcquireDedicatedViewAsync("SERIAL");
        await suspendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(acquire.IsCompleted);
        allowSuspend.TrySetResult();
        IAsyncDisposable lease = await acquire;
        Assert.IsTrue(coordinator.IsDedicatedViewActive("serial"));
        await lease.DisposeAsync();
    }

    [TestMethod]
    public async Task Release_ResumesMultiViewExactlyOnce()
    {
        ViewDevicePresentationCoordinator coordinator = new();
        int resumeCount = 0;
        using IDisposable registration = coordinator.RegisterMultiView(
            (_, _) => Task.CompletedTask,
            (_, _) =>
            {
                Interlocked.Increment(ref resumeCount);
                return Task.CompletedTask;
            });
        IAsyncDisposable lease = await coordinator.AcquireDedicatedViewAsync("SERIAL");

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.AreEqual(1, resumeCount);
    }

    [TestMethod]
    public async Task Unregister_PreventsResumeCallbackAfterDedicatedClose()
    {
        ViewDevicePresentationCoordinator coordinator = new();
        int resumeCount = 0;
        IDisposable registration = coordinator.RegisterMultiView(
            (_, _) => Task.CompletedTask,
            (_, _) =>
            {
                Interlocked.Increment(ref resumeCount);
                return Task.CompletedTask;
            });
        IAsyncDisposable lease = await coordinator.AcquireDedicatedViewAsync("SERIAL");

        registration.Dispose();
        await lease.DisposeAsync();

        Assert.AreEqual(0, resumeCount);
    }

    [TestMethod]
    public async Task LeaseAcquiredBeforeMultiRegistration_ResumesLatestRegistration()
    {
        ViewDevicePresentationCoordinator coordinator = new();
        int resumeCount = 0;
        IAsyncDisposable lease = await coordinator.AcquireDedicatedViewAsync("SERIAL");
        using IDisposable registration = coordinator.RegisterMultiView(
            (_, _) => Task.CompletedTask,
            (_, _) =>
            {
                Interlocked.Increment(ref resumeCount);
                return Task.CompletedTask;
            });

        await lease.DisposeAsync();

        Assert.AreEqual(1, resumeCount);
    }

    [TestMethod]
    public async Task FailedAcquire_RollsBackMultiViewSuspension()
    {
        ViewDevicePresentationCoordinator coordinator = new();
        int resumeCount = 0;
        using IDisposable registration = coordinator.RegisterMultiView(
            (_, _) => throw new InvalidOperationException("suspend failed"),
            (_, _) =>
            {
                Interlocked.Increment(ref resumeCount);
                return Task.CompletedTask;
            });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => coordinator.AcquireDedicatedViewAsync("SERIAL"));

        Assert.IsFalse(coordinator.IsDedicatedViewActive("SERIAL"));
        Assert.AreEqual(1, resumeCount);
    }

    [TestMethod]
    public async Task MultiViewRegistration_ReplacedWhileDedicatedActive_ReleaseResumesCurrentActiveRegistration()
    {
        ViewDevicePresentationCoordinator coordinator = new();
        int oldResumeCount = 0;
        int currentResumeCount = 0;
        IDisposable firstRegistration = coordinator.RegisterMultiView(
            (_, _) => Task.CompletedTask,
            (_, _) =>
            {
                Interlocked.Increment(ref oldResumeCount);
                return Task.CompletedTask;
            });
        IAsyncDisposable lease = await coordinator.AcquireDedicatedViewAsync("SERIAL");

        using IDisposable currentRegistration = coordinator.RegisterMultiView(
            (_, _) => Task.CompletedTask,
            (_, _) =>
            {
                Interlocked.Increment(ref currentResumeCount);
                return Task.CompletedTask;
            });
        firstRegistration.Dispose();

        await lease.DisposeAsync();

        Assert.AreEqual(0, oldResumeCount);
        Assert.AreEqual(1, currentResumeCount);
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
