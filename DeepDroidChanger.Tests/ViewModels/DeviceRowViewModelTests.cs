using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class DeviceRowViewModelTests
{
    [TestMethod]
    public void ConnectionStatus_RaisesPropertyChangedWhenStatusChanges()
    {
        var row = new DeviceRowViewModel(1, false, "serial", "name", "type", "Active", AdbDeviceStatus.Offline, "Offline", "Ready");
        var changedProperties = new List<string?>();
        row.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        row.ConnectionStatus = AdbDeviceStatus.Online;

        Assert.AreEqual(AdbDeviceStatus.Online, row.ConnectionStatus);
        Assert.AreEqual(1, changedProperties.Count);
        Assert.AreEqual(nameof(DeviceRowViewModel.ConnectionStatus), changedProperties[0]);
    }

    [TestMethod]
    public void RestoreProcess_CopiesTerminalMessageAndStateToRecreatedRow()
    {
        var row = new DeviceRowViewModel(1, false, "serial", "name", "type", "Active", AdbDeviceStatus.Online, "Online", "Ready...");

        row.RestoreProcess("Device change completed", DeviceProcessState.Succeeded);

        Assert.AreEqual("Device change completed", row.Process);
        Assert.AreEqual(DeviceProcessState.Succeeded, row.ProcessState);
    }
}
