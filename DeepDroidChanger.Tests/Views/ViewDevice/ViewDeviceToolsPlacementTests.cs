using System.Windows;
using DeepDroidChanger.Views;

namespace DeepDroidChanger.Tests.Views.ViewDevice;

[TestClass]
public sealed class ViewDeviceToolsPlacementTests
{
    [TestMethod]
    public void Calculate_PrefersRightWhenThereIsEnoughSpace()
    {
        Point result = ViewDeviceToolsPlacement.Calculate(
            new Rect(100, 120, 520, 700),
            new Size(310, 700),
            new Rect(0, 0, 1920, 1040),
            12);

        Assert.AreEqual(632, result.X, 0.001);
        Assert.AreEqual(120, result.Y, 0.001);
    }

    [TestMethod]
    public void CalculateFallsBackToLeftWhenRightDoesNotFit()
    {
        Point result = ViewDeviceToolsPlacement.Calculate(
            new Rect(900, 120, 520, 700),
            new Size(310, 700),
            new Rect(0, 0, 1600, 1040),
            12);

        Assert.AreEqual(578, result.X, 0.001);
        Assert.AreEqual(120, result.Y, 0.001);
    }

    [TestMethod]
    public void CalculateClampsWhenNeitherSideFits()
    {
        Point result = ViewDeviceToolsPlacement.Calculate(
            new Rect(100, 900, 520, 200),
            new Size(310, 500),
            new Rect(0, 0, 800, 1000),
            12);

        Assert.AreEqual(490, result.X, 0.001);
        Assert.AreEqual(500, result.Y, 0.001);
    }

    [TestMethod]
    public void Calculate_CompensatesOuterMarginForVisibleTwelveDipGap()
    {
        Rect owner = new(100, 120, 520, 656);
        const double rootBorderMargin = 8;

        Point result = ViewDeviceToolsPlacement.Calculate(
            owner,
            new Size(310, 656),
            new Rect(0, 0, 1920, 1040),
            visibleGap: 12,
            shadowMargin: rootBorderMargin);

        Assert.AreEqual(12, result.X + rootBorderMargin - owner.Right, 0.001);
    }

    [TestMethod]
    public void DetailCalculate_FollowsToolsOnOutwardRightSide()
    {
        Point result = ViewDeviceToolDetailPlacement.Calculate(
            new Rect(100, 120, 520, 656),
            new Rect(632, 120, 310, 656),
            new Size(350, 300),
            new Rect(0, 0, 1920, 1040),
            visibleGap: 12,
            shadowMargin: 8);

        Assert.AreEqual(946, result.X, 0.001);
        Assert.AreEqual(120, result.Y, 0.001);
    }

    [TestMethod]
    public void DetailCalculate_UsesOppositeSideAndClampsToMonitor()
    {
        Point result = ViewDeviceToolDetailPlacement.Calculate(
            new Rect(900, 120, 520, 656),
            new Rect(578, 120, 310, 656),
            new Size(350, 300),
            new Rect(0, 0, 1000, 700),
            visibleGap: 12,
            shadowMargin: 8);

        Assert.AreEqual(224, result.X, 0.001);
        Assert.AreEqual(120, result.Y, 0.001);
    }
}
