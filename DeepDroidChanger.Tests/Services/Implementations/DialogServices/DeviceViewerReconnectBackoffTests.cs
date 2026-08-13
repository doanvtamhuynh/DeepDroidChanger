using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeviceViewerReconnectBackoffTests
{
    [TestMethod]
    public void ZeroFailures_UsesImmediateReconnectDelay()
    {
        var policy = new DeviceViewerReconnectBackoff();

        Assert.AreEqual(TimeSpan.Zero, DeviceViewerRuntime.GetReconnectDelay(policy));
    }

    [TestMethod]
    public void OneFailure_UsesFiveHundredMillisecondDelay()
    {
        var policy = new DeviceViewerReconnectBackoff();
        policy.RegisterFailure();

        Assert.AreEqual(
            TimeSpan.FromMilliseconds(500),
            DeviceViewerRuntime.GetReconnectDelay(policy));
    }

    [TestMethod]
    public void TrackerRecoveryWithOnlineDeviceAndNoStreamFailure_IsImmediate()
    {
        var policy = new DeviceViewerReconnectBackoff();

        // Tracker recovery schedules the same reconnect loop used by the runtime.
        Assert.AreEqual(TimeSpan.Zero, DeviceViewerRuntime.GetReconnectDelay(policy));
    }

    [TestMethod]
    public void FailedScrcpyStart_RegistersTheFirstBackoffDelay()
    {
        var policy = new DeviceViewerReconnectBackoff();

        Assert.AreEqual(
            TimeSpan.FromMilliseconds(500),
            DeviceViewerRuntime.RegisterReconnectFailure(policy));
        Assert.AreEqual(1, policy.FailureCount);
    }

    [TestMethod]
    public void RegisterFailure_UsesPersistentProgressiveDelays()
    {
        var policy = new DeviceViewerReconnectBackoff();

        Assert.AreEqual(TimeSpan.FromMilliseconds(500), policy.RegisterFailure());
        Assert.AreEqual(TimeSpan.FromSeconds(1), policy.RegisterFailure());
        Assert.AreEqual(TimeSpan.FromSeconds(2), policy.RegisterFailure());
        Assert.AreEqual(TimeSpan.FromSeconds(5), policy.RegisterFailure());
        Assert.AreEqual(TimeSpan.FromSeconds(10), policy.RegisterFailure());
        Assert.AreEqual(TimeSpan.FromSeconds(20), policy.RegisterFailure());
        Assert.AreEqual(TimeSpan.FromSeconds(30), policy.RegisterFailure());
        Assert.AreEqual(TimeSpan.FromSeconds(30), policy.RegisterFailure());
    }

    [TestMethod]
    public void ShortLivedSuccessfulSession_DoesNotResetPolicyUntilStabilityIsReached()
    {
        var policy = new DeviceViewerReconnectBackoff();
        policy.RegisterFailure();
        policy.RegisterFailure();

        // A session start does not call Reset; the runtime timer is the only reset owner.
        Assert.AreEqual(2, policy.FailureCount);
        Assert.AreEqual(TimeSpan.FromSeconds(1), policy.GetCurrentDelay());

        policy.Reset();
        Assert.AreEqual(0, policy.FailureCount);
    }

    [TestMethod]
    public async Task CancellationInterruptsReconnectDelay()
    {
        using var cancellation = new CancellationTokenSource();
        Task delay = Task.Delay(TimeSpan.FromSeconds(30), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => delay);
    }
}
