using SharpAdbClient;
using ScrcpyNet;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.Tests.Helpers;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
[DoNotParallelize]
public sealed class ScrcpyNetDeviceLeaseTests
{
    [TestMethod]
    public void DifferentSerials_CanAcquireSimultaneously()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();

        using IDisposable first = coordinator.Acquire("A");
        using IDisposable second = coordinator.Acquire("B");
    }

    [TestMethod]
    public void SameSerial_SecondAcquireThrows()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        using IDisposable first = coordinator.Acquire("SERIAL");

        Assert.ThrowsExactly<ScrcpyNetDeviceBusyException>(
            () => coordinator.Acquire("SERIAL"));
    }

    [TestMethod]
    public void SerialComparison_IsCaseInsensitive()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        using IDisposable first = coordinator.Acquire("Serial");

        ScrcpyNetDeviceBusyException exception =
            Assert.ThrowsExactly<ScrcpyNetDeviceBusyException>(
                () => coordinator.Acquire("sErIaL"));

        Assert.AreEqual("sErIaL", exception.Serial);
    }

    [TestMethod]
    public void Release_AllowsReacquire()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        IDisposable first = coordinator.Acquire("SERIAL");

        first.Dispose();
        using IDisposable second = coordinator.Acquire("serial");
    }

    [TestMethod]
    public void DoubleRelease_IsSafe()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        IDisposable first = coordinator.Acquire("SERIAL");

        first.Dispose();
        first.Dispose();
        using IDisposable second = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public void ClientFactoryFailure_ReleasesLease()
    {
        using TestTempDirectory temporary = new();
        string adbPath = Path.Combine(temporary.Path, "adb.exe");
        File.WriteAllBytes(adbPath, [0]);
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        ScrcpyNetClientFactory factory = new(
            new ScrcpyNetRuntimeResolver(temporary.Path, adbPath),
            coordinator);

        Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => factory.Create(
                CreateDevice("SERIAL"),
                new ViewDeviceLaunchOptions("SERIAL")));

        using IDisposable lease = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public void ClientDispose_ReleasesLease()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        IDisposable lease = coordinator.Acquire("SERIAL");
        int underlyingDisposeCount = 0;
        using ScrcpyNetClient client = new(
            lease,
            static _ => Task.CompletedTask,
            () => underlyingDisposeCount++);

        client.Dispose();
        client.Dispose();

        Assert.AreEqual(1, underlyingDisposeCount);
        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public async Task NormalStop_ReleasesLease()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        SignaledLease lease = new(coordinator.Acquire("SERIAL"));
        using ScrcpyNetClient client = new(
            lease,
            static _ => Task.CompletedTask,
            static () => { },
            Task.CompletedTask);

        client.Dispose();
        await lease.Released.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public async Task ServerTaskPendingAfterShutdownTimeout_SameSerialRemainsBusy()
    {
        TaskCompletionSource serverCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        SignaledLease lease = new(coordinator.Acquire("SERIAL"));
        using ScrcpyNetClient client = new(
            lease,
            static _ => Task.CompletedTask,
            static () => { },
            serverCompletion.Task);

        client.Dispose();

        Assert.IsFalse(lease.Released.Task.IsCompleted);
        Assert.ThrowsExactly<ScrcpyNetDeviceBusyException>(
            () => coordinator.Acquire("SERIAL"));

        serverCompletion.TrySetResult();
        await lease.Released.Task.WaitAsync(TimeSpan.FromSeconds(1));
        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public async Task ServerTaskPendingAfterShutdownTimeout_DifferentSerialUnaffected()
    {
        TaskCompletionSource serverCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        SignaledLease lease = new(coordinator.Acquire("SERIAL-A"));
        using ScrcpyNetClient client = new(
            lease,
            static _ => Task.CompletedTask,
            static () => { },
            serverCompletion.Task);

        client.Dispose();

        using IDisposable differentSerial = coordinator.Acquire("SERIAL-B");
        Assert.ThrowsExactly<ScrcpyNetDeviceBusyException>(
            () => coordinator.Acquire("SERIAL-A"));

        serverCompletion.TrySetResult();
        await lease.Released.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task ServerTaskCompletesLater_LeaseReleases()
    {
        TaskCompletionSource serverCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        SignaledLease lease = new(coordinator.Acquire("SERIAL"));
        using ScrcpyNetClient client = new(
            lease,
            static _ => Task.CompletedTask,
            static () => { },
            serverCompletion.Task);

        client.Dispose();
        serverCompletion.TrySetResult();
        await lease.Released.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public async Task DelayedServerCompletion_ReleasesLeaseExactlyOnce()
    {
        TaskCompletionSource serverCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        SignaledLease lease = new(coordinator.Acquire("SERIAL"));
        using ScrcpyNetClient client = new(
            lease,
            static _ => Task.CompletedTask,
            static () => { },
            serverCompletion.Task);

        client.Dispose();
        client.Dispose();
        Assert.IsFalse(lease.Released.Task.IsCompleted);

        serverCompletion.TrySetResult();
        await lease.Released.Task.WaitAsync(TimeSpan.FromSeconds(1));
        lease.Dispose();

        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public async Task FailedStart_EventualDispose_ReleasesLease()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        IDisposable lease = coordinator.Acquire("SERIAL");
        int underlyingDisposeCount = 0;
        using ScrcpyNetClient client = new(
            lease,
            static _ => Task.FromException(
                new FileNotFoundException("The fake Scrcpy server was not found.")),
            () => underlyingDisposeCount++);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => client.StartAsync(CancellationToken.None));
        client.Dispose();

        Assert.AreEqual(1, underlyingDisposeCount);
        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public async Task CanceledStart_EventualDispose_ReleasesLease()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        IDisposable lease = coordinator.Acquire("SERIAL");
        int underlyingDisposeCount = 0;
        using ScrcpyNetClient client = new(
            lease,
            static cancellationToken => Task.FromCanceled(cancellationToken),
            () => underlyingDisposeCount++);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.StartAsync(cancellation.Token));
        client.Dispose();

        Assert.AreEqual(1, underlyingDisposeCount);
        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public void EightDifferentSerials_CanAcquire()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        IDisposable[] leases = Enumerable.Range(1, 8)
            .Select(index => coordinator.Acquire($"SERIAL-{index}"))
            .ToArray();

        foreach (IDisposable lease in leases)
            lease.Dispose();
    }

    [TestMethod]
    public void SingleViewAndMultipleView_SameSerial_CannotCreateTwoClients()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        using IDisposable multipleViewLease = coordinator.Acquire("SERIAL");
        ScrcpyNetClientFactory factory = new(
            new ScrcpyNetRuntimeResolver(Path.GetTempPath(), "missing-adb.exe"),
            coordinator);

        Assert.ThrowsExactly<ScrcpyNetDeviceBusyException>(
            () => factory.Create(
                CreateDevice("SERIAL"),
                new ViewDeviceLaunchOptions("SERIAL")));
    }

    [TestMethod]
    public void Busy_DoesNotStealOtherOwner()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        using IDisposable owner = coordinator.Acquire("SERIAL");

        Assert.ThrowsExactly<ScrcpyNetDeviceBusyException>(
            () => coordinator.Acquire("SERIAL"));

        using IDisposable otherSerial = coordinator.Acquire("OTHER");
        Assert.ThrowsExactly<ScrcpyNetDeviceBusyException>(
            () => coordinator.Acquire("SERIAL"));
    }

    [TestMethod]
    public void DifferentSerialSingleAndMultiple_CanCoexist()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();

        using IDisposable singleViewLease = coordinator.Acquire("SINGLE");
        using IDisposable multipleViewLease = coordinator.Acquire("MULTIPLE");
    }

    [TestMethod]
    public void EightMultiSerials_CanOwnEightLeases()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        IDisposable[] leases = Enumerable.Range(1, 8)
            .Select(index => coordinator.Acquire($"MULTI-{index}"))
            .ToArray();

        Assert.AreEqual(8, leases.Length);
        foreach (IDisposable lease in leases)
            lease.Dispose();
    }

    [TestMethod]
    public void FailedClientConstruction_ReleasesLease()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        ScrcpyNetClientFactory factory = new(
            new ScrcpyNetRuntimeResolver(Path.GetTempPath(), "missing-adb.exe"),
            coordinator);

        Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => factory.Create(
                CreateDevice("SERIAL"),
                new ViewDeviceLaunchOptions("SERIAL")));

        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public async Task FailedServerStart_EventualCleanupReleasesLease()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        IDisposable lease = coordinator.Acquire("SERIAL");
        int underlyingDisposeCount = 0;
        using ScrcpyNetClient client = new(
            lease,
            static _ => Task.FromException(new InvalidOperationException("server start failed")),
            () => underlyingDisposeCount++);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.StartAsync(CancellationToken.None));
        client.Dispose();

        Assert.AreEqual(1, underlyingDisposeCount);
        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public void DisposedClient_ReleasesLeaseExactlyOnce()
    {
        ScrcpyNetDeviceLeaseCoordinator coordinator = new();
        IDisposable lease = coordinator.Acquire("SERIAL");
        int underlyingDisposeCount = 0;
        using ScrcpyNetClient client = new(
            lease,
            static _ => Task.CompletedTask,
            () => underlyingDisposeCount++);

        client.Dispose();
        client.Dispose();

        Assert.AreEqual(1, underlyingDisposeCount);
        using IDisposable reacquired = coordinator.Acquire("SERIAL");
    }

    [TestMethod]
    public void Factory_UsesMultiViewProfile()
    {
        ViewDeviceLaunchOptions options = ViewDeviceLaunchOptions.ForMultiView("SERIAL");

        Assert.AreEqual(720, options.MaxSize);
        Assert.AreEqual(20, options.MaxFps);
        Assert.AreEqual("2M", options.VideoBitRate);
        Assert.IsFalse(options.NoControl);
        Assert.IsTrue(options.NoAudio);
        Assert.AreEqual(2_000_000L, ScrcpyNetClientFactory.ParseBitrate(options.VideoBitRate));
    }

    private static DeviceData CreateDevice(string serial)
    {
        return new DeviceData { Serial = serial };
    }

    private sealed class SignaledLease(IDisposable inner) : IDisposable
    {
        private int disposed;

        public TaskCompletionSource Released { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            inner.Dispose();
            Released.TrySetResult();
        }
    }
}
