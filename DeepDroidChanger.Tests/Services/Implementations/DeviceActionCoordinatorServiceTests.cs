using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class DeviceActionCoordinatorServiceTests
{
    [TestMethod]
    public void TryStart_OnlyOneOperationPerSerial_AndDifferentSerialsRemainIndependent()
    {
        var service = new DeviceActionCoordinatorService();

        using IDeviceActionOperation? first = service.TryStart(
            " SERIAL-A ",
            DeviceActionKind.ChangeDevice,
            canCancel: true);
        using IDeviceActionOperation? duplicate = service.TryStart(
            "serial-a",
            DeviceActionKind.Wipe,
            canCancel: true);
        using IDeviceActionOperation? other = service.TryStart(
            "SERIAL-B",
            DeviceActionKind.Wipe,
            canCancel: true);

        Assert.IsNotNull(first);
        Assert.IsNull(duplicate);
        Assert.IsNotNull(other);
        Assert.IsTrue(service.IsBusy("serial-a"));
        Assert.IsTrue(service.IsBusy("serial-b"));
    }

    [TestMethod]
    public void TryRequestCancellation_TransitionsToStoppingAndKeepsSerialBusyUntilDispose()
    {
        var service = new DeviceActionCoordinatorService();
        using IDeviceActionOperation operation = service.TryStart(
            "SERIAL-A",
            DeviceActionKind.ChangeDevice,
            canCancel: true)!;

        Assert.AreEqual(DeviceActionRuntimeState.Running, operation.State);
        Assert.AreEqual(DeviceActionCancellationReason.None, operation.CancellationReason);
        Assert.IsTrue(service.TryRequestCancellation("serial-a"));
        Assert.AreEqual(DeviceActionRuntimeState.Stopping, operation.State);
        Assert.AreEqual(DeviceActionCancellationReason.UserStop, operation.CancellationReason);
        Assert.IsTrue(operation.IsCancellationRequested);
        Assert.IsTrue(service.IsBusy("SERIAL-A"));
        Assert.IsFalse(service.TryRequestCancellation("SERIAL-A"));

        operation.Dispose();
        Assert.IsFalse(service.IsBusy("SERIAL-A"));
        Assert.AreEqual(DeviceActionRuntimeState.Idle, operation.State);
        operation.Dispose();
    }

    [TestMethod]
    public void TryStart_LinksExternalCancellationAndDoesNotAllowExplicitCancelWhenUnsupported()
    {
        var external = new CancellationTokenSource();
        var service = new DeviceActionCoordinatorService();
        using IDeviceActionOperation operation = service.TryStart(
            "SERIAL-A",
            DeviceActionKind.DeleteDevice,
            canCancel: false,
            external.Token)!;

        Assert.IsFalse(service.TryRequestCancellation("SERIAL-A"));
        external.Cancel();

        Assert.IsTrue(operation.IsCancellationRequested);
        Assert.AreEqual(DeviceActionRuntimeState.Stopping, operation.State);
        Assert.AreEqual(DeviceActionCancellationReason.External, operation.CancellationReason);
        Assert.IsTrue(service.IsBusy("SERIAL-A"));
    }

    [TestMethod]
    public void CancellationReason_FirstTransitionWins()
    {
        var external = new CancellationTokenSource();
        var service = new DeviceActionCoordinatorService();
        using IDeviceActionOperation operation = service.TryStart(
            "SERIAL-A",
            DeviceActionKind.ChangeDevice,
            canCancel: true,
            external.Token)!;

        Assert.IsTrue(service.TryRequestCancellation("SERIAL-A"));
        external.Cancel();

        Assert.AreEqual(DeviceActionCancellationReason.UserStop, operation.CancellationReason);
        Assert.AreEqual(
            DeviceActionCancellationReason.UserStop,
            service.GetOperation("SERIAL-A")!.CancellationReason);
    }

    [TestMethod]
    public void ExternalCancellationReason_IsNotOverwrittenByLaterUserStop()
    {
        var external = new CancellationTokenSource();
        var service = new DeviceActionCoordinatorService();
        using IDeviceActionOperation operation = service.TryStart(
            "SERIAL-A",
            DeviceActionKind.ChangeDevice,
            canCancel: true,
            external.Token)!;

        external.Cancel();
        Assert.IsFalse(service.TryRequestCancellation("SERIAL-A"));

        Assert.AreEqual(DeviceActionCancellationReason.External, operation.CancellationReason);
        Assert.AreEqual(DeviceActionRuntimeState.Stopping, operation.State);
    }

    [TestMethod]
    public async Task TryStart_ConcurrentAttemptsForSameSerial_AllowsExactlyOneOperation()
    {
        const int attemptCount = 16;
        var service = new DeviceActionCoordinatorService();
        using var startGate = new ManualResetEventSlim(initialState: false);
        Task<IDeviceActionOperation?>[] attempts = Enumerable.Range(0, attemptCount)
            .Select(_ => Task.Run(() =>
            {
                startGate.Wait();
                return service.TryStart(
                    "SERIAL-A",
                    DeviceActionKind.ChangeDevice,
                    canCancel: true);
            }))
            .ToArray();

        startGate.Set();
        IDeviceActionOperation?[] operations = await Task.WhenAll(attempts);
        IDeviceActionOperation[] winners = operations.OfType<IDeviceActionOperation>().ToArray();

        Assert.HasCount(1, winners);
        Assert.IsTrue(service.IsBusy("SERIAL-A"));

        winners[0].Dispose();
        Assert.IsFalse(service.IsBusy("SERIAL-A"));
    }

    [TestMethod]
    public void Dispose_StaleOperationCannotReleaseNewOwnerForSameSerial()
    {
        var service = new DeviceActionCoordinatorService();
        IDeviceActionOperation first = service.TryStart(
            "SERIAL-A",
            DeviceActionKind.ChangeDevice,
            canCancel: true)!;
        first.Dispose();

        using IDeviceActionOperation second = service.TryStart(
            "SERIAL-A",
            DeviceActionKind.Wipe,
            canCancel: true)!;
        first.Dispose();

        Assert.IsTrue(service.IsBusy("SERIAL-A"));
        Assert.AreEqual(second.OperationId, service.GetOperation("SERIAL-A")!.OperationId);
    }

    [TestMethod]
    public void OperationStateChanged_NotifiesRunningStoppingAndIdleOutsideOwnershipLock()
    {
        var service = new DeviceActionCoordinatorService();
        var states = new List<DeviceActionRuntimeState>();
        service.OperationStateChanged += snapshot =>
        {
            states.Add(snapshot.State);
            Assert.IsTrue(service.IsBusy(snapshot.Serial) || snapshot.State == DeviceActionRuntimeState.Idle);
        };

        using IDeviceActionOperation operation = service.TryStart(
            "SERIAL-A",
            DeviceActionKind.ChangeDevice,
            canCancel: true)!;
        service.TryRequestCancellation("SERIAL-A");
        operation.Dispose();

        CollectionAssert.AreEqual(
            new[]
            {
                DeviceActionRuntimeState.Running,
                DeviceActionRuntimeState.Stopping,
                DeviceActionRuntimeState.Idle
            },
            states);
    }

    [TestMethod]
    public async Task OperationStateChanged_SlowSubscriberDoesNotBlockUnrelatedOwnership()
    {
        var service = new DeviceActionCoordinatorService();
        using var subscriberEntered = new ManualResetEventSlim(initialState: false);
        using var releaseSubscriber = new ManualResetEventSlim(initialState: false);
        service.OperationStateChanged += snapshot =>
        {
            if (snapshot.Serial != "SERIAL-A" || snapshot.State != DeviceActionRuntimeState.Running)
                return;

            subscriberEntered.Set();
            releaseSubscriber.Wait();
        };

        Task<IDeviceActionOperation?> firstStart = Task.Run(() => service.TryStart(
            "SERIAL-A",
            DeviceActionKind.ChangeDevice,
            canCancel: true));
        IDeviceActionOperation? second = null;
        try
        {
            Assert.IsTrue(subscriberEntered.Wait(TimeSpan.FromSeconds(5)));
            second = await Task.Run(() => service.TryStart(
                    "SERIAL-B",
                    DeviceActionKind.Wipe,
                    canCancel: true))
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsNotNull(second);
        }
        finally
        {
            releaseSubscriber.Set();
        }

        using IDeviceActionOperation? first = await firstStart;
        second?.Dispose();
    }

    [TestMethod]
    public void OperationStateChanged_ThrowingSubscriberDoesNotLeakOwnershipOrBlockOtherSubscribers()
    {
        var service = new DeviceActionCoordinatorService();
        var observedStates = new List<DeviceActionRuntimeState>();
        service.OperationStateChanged += _ => throw new InvalidOperationException("Subscriber failure.");
        service.OperationStateChanged += snapshot => observedStates.Add(snapshot.State);

        IDeviceActionOperation operation = service.TryStart(
            "SERIAL-A",
            DeviceActionKind.ChangeDevice,
            canCancel: true)!;
        Assert.IsTrue(service.TryRequestCancellation("SERIAL-A"));
        operation.Dispose();

        Assert.IsFalse(service.IsBusy("SERIAL-A"));
        CollectionAssert.AreEqual(
            new[]
            {
                DeviceActionRuntimeState.Running,
                DeviceActionRuntimeState.Stopping,
                DeviceActionRuntimeState.Idle
            },
            observedStates);
    }

    [TestMethod]
    public void DeviceActionKind_IsBatchAction_ClassifiesOnlyBatchKinds()
    {
        Assert.IsTrue(DeviceActionKind.BatchRandomDevice.IsBatchAction());
        Assert.IsTrue(DeviceActionKind.BatchRandomChangeAndWipe.IsBatchAction());
        Assert.IsTrue(DeviceActionKind.BatchInstallPackages.IsBatchAction());
        Assert.IsFalse(DeviceActionKind.RandomDevice.IsBatchAction());
        Assert.IsFalse(DeviceActionKind.RandomChangeAndWipe.IsBatchAction());
        Assert.IsFalse(DeviceActionKind.ChangeDevice.IsBatchAction());
    }

    [TestMethod]
    public void DeviceActionKind_ToLogicalActionKind_MapsBatchKindsForPresentationOnly()
    {
        Assert.AreEqual(
            DeviceActionKind.RandomDevice,
            DeviceActionKind.BatchRandomDevice.ToLogicalActionKind());
        Assert.AreEqual(
            DeviceActionKind.RandomChangeAndWipe,
            DeviceActionKind.BatchRandomChangeAndWipe.ToLogicalActionKind());
        Assert.AreEqual(
            DeviceActionKind.ChangeDevice,
            DeviceActionKind.BatchChangeDevice.ToLogicalActionKind());
        Assert.AreEqual(
            DeviceActionKind.InstallPackages,
            DeviceActionKind.BatchInstallPackages.ToLogicalActionKind());
        Assert.AreEqual(
            DeviceActionKind.FakeProxy,
            DeviceActionKind.FakeProxy.ToLogicalActionKind());
    }

    [TestMethod]
    public void ActiveSessions_KeepSeparateInvocationsAndCancelOnlyRequestedSession()
    {
        var service = new DeviceActionCoordinatorService();
        Guid firstSession = Guid.NewGuid();
        Guid secondSession = Guid.NewGuid();
        using IDeviceActionOperation first = service.TryStart(
            "SERIAL-A",
            DeviceActionKind.BatchRandomDevice,
            canCancel: true,
            sessionId: firstSession)!;
        using IDeviceActionOperation second = service.TryStart(
            "SERIAL-B",
            DeviceActionKind.BatchRandomDevice,
            canCancel: true,
            sessionId: firstSession)!;
        using IDeviceActionOperation third = service.TryStart(
            "SERIAL-C",
            DeviceActionKind.BatchRandomDevice,
            canCancel: true,
            sessionId: secondSession)!;

        IReadOnlyList<DeviceActionSessionSnapshot> sessions = service.GetActiveSessions();
        Assert.HasCount(2, sessions);
        Assert.HasCount(2, sessions.Single(session => session.SessionId == firstSession).Operations);
        Assert.HasCount(1, sessions.Single(session => session.SessionId == secondSession).Operations);

        Assert.IsTrue(service.TryRequestSessionCancellation(firstSession));
        Assert.AreEqual(DeviceActionRuntimeState.Stopping, first.State);
        Assert.AreEqual(DeviceActionRuntimeState.Stopping, second.State);
        Assert.AreEqual(DeviceActionRuntimeState.Running, third.State);
        Assert.IsFalse(service.TryRequestSessionCancellation(firstSession));
    }
}
