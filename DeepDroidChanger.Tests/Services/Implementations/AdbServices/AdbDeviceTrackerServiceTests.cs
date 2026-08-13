using DeepDroidChanger.Services;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class AdbDeviceTrackerServiceTests
{
    [TestMethod]
    public void AdvanceReconnectAttempt_BeforeValidSnapshot_ContinuesBackoff()
    {
        Assert.AreEqual(1, AdbDeviceTrackerService.AdvanceReconnectAttempt(0, false));
        Assert.AreEqual(2, AdbDeviceTrackerService.AdvanceReconnectAttempt(1, false));
        Assert.AreEqual(4, AdbDeviceTrackerService.AdvanceReconnectAttempt(3, false));
        Assert.AreEqual(4, AdbDeviceTrackerService.AdvanceReconnectAttempt(4, false));
    }

    [TestMethod]
    public void AdvanceReconnectAttempt_AfterValidSnapshot_ResetsBackoff()
    {
        Assert.AreEqual(0, AdbDeviceTrackerService.GetReconnectDelayAttempt(4, true));
        Assert.AreEqual(1, AdbDeviceTrackerService.AdvanceReconnectAttempt(4, true));
    }

    [TestMethod]
    public void AdvanceReconnectAttempt_TransportSuccessWithoutSnapshot_DoesNotResetBackoff()
    {
        var attempt = AdbDeviceTrackerService.AdvanceReconnectAttempt(2, false);

        Assert.AreEqual(3, attempt);
    }

    [TestMethod]
    public void TrackerHealth_OkayWithoutSnapshot_RemainsReconnecting()
    {
        Assert.AreEqual(
            AdbDeviceTrackerHealth.Reconnecting,
            AdbDeviceTrackerService.DetermineHealthAfterTrackResponse(
                transportAccepted: true,
                validSnapshotPublished: false));
    }

    [TestMethod]
    public void TrackerHealth_ValidEmptySnapshotBecomesConnected()
    {
        Assert.AreEqual(
            AdbDeviceTrackerHealth.Connected,
            AdbDeviceTrackerService.DetermineHealthAfterTrackResponse(
                transportAccepted: true,
                validSnapshotPublished: true));
    }

    [TestMethod]
    public void TrackerHealth_TransportFailureAfterSnapshotReturnsToReconnecting()
    {
        Assert.AreEqual(
            AdbDeviceTrackerHealth.Reconnecting,
            AdbDeviceTrackerService.DetermineHealthAfterTrackResponse(
                transportAccepted: false,
                validSnapshotPublished: true));
    }

    [TestMethod]
    public async Task FirstSnapshotWaiter_ObservesConnectedHealthAfterCompletion()
    {
        var health = AdbDeviceTrackerHealth.Reconnecting;
        var snapshotPublished = false;
        var firstSnapshot = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AdbDeviceTrackerHealth> waiter = firstSnapshot.Task.ContinueWith(
            _ =>
            {
                Assert.IsTrue(snapshotPublished);
                return health;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        AdbDeviceTrackerService.CompleteFirstSnapshot(
            () => snapshotPublished = true,
            () => health = AdbDeviceTrackerHealth.Connected,
            firstSnapshot);

        Assert.AreEqual(AdbDeviceTrackerHealth.Connected, await waiter);
    }

    [TestMethod]
    public void EmptyTrackedSnapshot_IsStillAValidFirstSnapshot()
    {
        Assert.HasCount(0, AdbDeviceService.ParseTrackedDevices(string.Empty));

        var firstSnapshot = new TaskCompletionSource<bool>();
        AdbDeviceTrackerService.CompleteFirstSnapshot(
            static () => { },
            static () => { },
            firstSnapshot);

        Assert.IsTrue(firstSnapshot.Task.IsCompletedSuccessfully);
    }
}
