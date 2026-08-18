using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class DeviceProcessStateServiceTests
{
    [TestMethod]
    public void SetProcess_StoresStateBySerial()
    {
        var service = new DeviceProcessStateService();

        service.SetProcess("A", "Changing device", "Log_ChangeDevice");

        DeviceProcessSnapshot snapshot = service.Get("A")!;
        Assert.AreEqual("A", snapshot.Serial);
        Assert.AreEqual("Changing device", snapshot.Message);
        Assert.AreEqual("Log_ChangeDevice", snapshot.ResourceKey);
        Assert.AreEqual(DeviceProcessState.InProgress, snapshot.State);
    }

    [TestMethod]
    public void SetProcess_IsCaseInsensitiveBySerial()
    {
        var service = new DeviceProcessStateService();

        service.SetProcess(" serial-a ", "Completed", "Log_ChangeDeviceSuccess");

        Assert.AreEqual("Completed", service.Get("SERIAL-A")!.Message);
    }

    [TestMethod]
    public void SetProcess_PublishesSnapshot()
    {
        var service = new DeviceProcessStateService();
        DeviceProcessSnapshot? published = null;
        service.ProcessChanged += snapshot => published = snapshot;

        service.SetProcess("A", "Canceled", "Log_ChangeDeviceCanceled");

        Assert.IsNotNull(published);
        Assert.AreEqual(DeviceProcessState.Canceled, published.State);
        Assert.AreEqual("Log_ChangeDeviceCanceled", published.ResourceKey);
    }

    [TestMethod]
    public void Ready_DoesNotOverwriteTerminalState()
    {
        var service = new DeviceProcessStateService();
        int publicationCount = 0;
        service.ProcessChanged += _ => publicationCount++;
        service.SetProcess("A", "Completed", "Log_ChangeDeviceSuccess");

        service.SetProcess("A", "Ready", "Log_Ready");

        Assert.AreEqual("Completed", service.Get("A")!.Message);
        Assert.AreEqual(DeviceProcessState.Succeeded, service.Get("A")!.State);
        Assert.AreEqual(1, publicationCount);
    }

    [TestMethod]
    public void NewInProgressAction_CanReplaceTerminalState()
    {
        var service = new DeviceProcessStateService();
        service.SetProcess("A", "Canceled", "Log_ChangeDeviceCanceled");

        service.SetProcess("A", "Changing", "Log_ChangeDevice");

        Assert.AreEqual("Changing", service.Get("A")!.Message);
        Assert.AreEqual(DeviceProcessState.InProgress, service.Get("A")!.State);
    }

    [TestMethod]
    public void Ready_ClearsTemporaryRunningMessage()
    {
        var service = new DeviceProcessStateService();
        service.SetProcess("A", "Changing", "Log_ChangeDevice");
        service.ShowTemporaryProcess(
            "A",
            "Already running",
            "Log_DeviceActionAlreadyRunningFormat",
            TimeSpan.FromSeconds(3));

        service.SetProcess("A", "Ready", "Log_Ready");

        Assert.AreEqual("Ready", service.Get("A")!.Message);
        Assert.AreEqual(DeviceProcessState.Ready, service.Get("A")!.State);
    }

    [TestMethod]
    public void DifferentSerials_AreIndependent()
    {
        var service = new DeviceProcessStateService();

        service.SetProcess("A", "Completed", "Log_ChangeDeviceSuccess");
        service.SetProcess("B", "Failed", "Log_ChangeDeviceFailed");
        service.SetProcess("A", "Ready", "Log_Ready");

        Assert.AreEqual(DeviceProcessState.Succeeded, service.Get("A")!.State);
        Assert.AreEqual(DeviceProcessState.Failed, service.Get("B")!.State);
    }

    [TestMethod]
    public void ThrowingSubscriber_DoesNotBlockOtherSubscribersOrLaterUpdates()
    {
        var service = new DeviceProcessStateService();
        var received = new List<string>();
        service.ProcessChanged += _ => throw new InvalidOperationException("subscriber failed");
        service.ProcessChanged += snapshot => received.Add(snapshot.Message);

        service.SetProcess("A", "First", "Log_ChangeDevice");
        service.SetProcess("A", "Second", "Log_ChangeDeviceSuccess");

        CollectionAssert.AreEqual(new[] { "First", "Second" }, received);
        Assert.AreEqual("Second", service.Get("A")!.Message);
    }
}
