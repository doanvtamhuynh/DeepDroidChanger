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

    [TestMethod]
    public void CalculateAspectFitSize_PreservesPortraitRatioWithinContainer()
    {
        var size = DeviceViewerDialog.CalculateAspectFitSize(1000, 600, 9d / 20d);

        Assert.AreEqual(270d, size.Width, 0.001d);
        Assert.AreEqual(600d, size.Height, 0.001d);
    }

    [TestMethod]
    public void CalculateAspectFitSize_UsesContainerWidthWhenItIsLimiting()
    {
        var size = DeviceViewerDialog.CalculateAspectFitSize(270, 1000, 9d / 20d);

        Assert.AreEqual(270d, size.Width, 0.001d);
        Assert.AreEqual(600d, size.Height, 0.001d);
    }
}
