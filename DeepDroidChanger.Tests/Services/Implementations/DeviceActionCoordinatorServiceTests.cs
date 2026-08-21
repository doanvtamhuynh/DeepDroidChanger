using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class DeviceActionCoordinatorServiceTests
{
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
