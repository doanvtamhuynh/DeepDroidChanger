using DeepDroidChanger.Views;

namespace DeepDroidChanger.Tests.Views.Dialogs;

[TestClass]
public sealed class DeviceViewerDialogLayoutTests
{
    [TestMethod]
    public void CalculateWindowMinWidth_PreservesDeviceAreaMinimum()
    {
        Assert.AreEqual(350d, DeviceViewerDialog.CalculateWindowMinWidth(isExpanded: false));
        Assert.AreEqual(587d, DeviceViewerDialog.CalculateWindowMinWidth(isExpanded: true));
    }

    [TestMethod]
    public void CalculateDesiredWindowWidth_AdjustsOnlyForActionsColumnDelta()
    {
        var expandedWidth = DeviceViewerDialog.CalculateDesiredWindowWidth(350, 52, 315);
        var collapsedWidth = DeviceViewerDialog.CalculateDesiredWindowWidth(expandedWidth, 315, 52);

        Assert.AreEqual(613d, expandedWidth);
        Assert.AreEqual(350d, collapsedWidth);
    }
}
