using System.Windows;
using DeepDroidChanger.Views;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class ViewMultipleDevicesLayoutTests
{
    [TestMethod]
    public void CalculateStreamHeight_PortraitAspect_DerivesTallHeight()
    {
        double height = ViewMultipleDevicesLayout.CalculateStreamHeight(320, 1080d / 2220d);

        Assert.AreEqual(320d / (1080d / 2220d), height, 0.000001);
    }

    [TestMethod]
    public void CalculateStreamHeight_LandscapeAspect_DerivesShortHeight()
    {
        double height = ViewMultipleDevicesLayout.CalculateStreamHeight(320, 2220d / 1080d);

        Assert.AreEqual(320d / (2220d / 1080d), height, 0.000001);
    }

    [TestMethod]
    public void CalculateStreamHeight_InvalidAspect_UsesNineBySixteenFallback()
    {
        double height = ViewMultipleDevicesLayout.CalculateStreamHeight(320, 0);

        Assert.AreEqual(320d / (9d / 16d), height, 0.000001);
    }

    [TestMethod]
    public void CalculateStreamHeight_InvalidWidth_ReturnsZero()
    {
        Assert.AreEqual(0, ViewMultipleDevicesLayout.CalculateStreamHeight(0, 9d / 16d));
        Assert.AreEqual(0, ViewMultipleDevicesLayout.CalculateStreamHeight(double.NaN, 9d / 16d));
        Assert.AreEqual(0, ViewMultipleDevicesLayout.CalculateStreamHeight(double.PositiveInfinity, 9d / 16d));
    }

    [TestMethod]
    public void CalculateStreamHeight_FallbackResult_IsFinite()
    {
        double height = ViewMultipleDevicesLayout.CalculateStreamHeight(
            500,
            double.PositiveInfinity);

        Assert.IsTrue(double.IsFinite(height));
        Assert.IsTrue(height > 0);
    }

    [TestMethod]
    public void CalculateVisibleClip_FullyVisible_ReturnsFullLocalRect()
    {
        Rect visibleClip = ViewMultipleDevicesLayout.CalculateVisibleClip(
            new Rect(10, 20, 320, 660),
            new Rect(0, 0, 1200, 900));

        AssertRect(visibleClip, 0, 0, 320, 660);
    }

    [TestMethod]
    public void CalculateVisibleClip_PartiallyBelowViewport_ClipsBottom()
    {
        Rect visibleClip = ViewMultipleDevicesLayout.CalculateVisibleClip(
            new Rect(0, 100, 320, 660),
            new Rect(0, 0, 1200, 600));

        AssertRect(visibleClip, 0, 0, 320, 500);
    }

    [TestMethod]
    public void CalculateVisibleClip_PartiallyAboveViewport_ClipsTop()
    {
        Rect visibleClip = ViewMultipleDevicesLayout.CalculateVisibleClip(
            new Rect(0, -120, 320, 660),
            new Rect(0, 0, 1200, 600));

        AssertRect(visibleClip, 0, 120, 320, 540);
    }

    [TestMethod]
    public void CalculateVisibleClip_PartiallyLeft_ClipsLeft()
    {
        Rect visibleClip = ViewMultipleDevicesLayout.CalculateVisibleClip(
            new Rect(-80, 20, 320, 660),
            new Rect(0, 0, 1200, 900));

        AssertRect(visibleClip, 80, 0, 240, 660);
    }

    [TestMethod]
    public void CalculateVisibleClip_PartiallyRight_ClipsRight()
    {
        Rect visibleClip = ViewMultipleDevicesLayout.CalculateVisibleClip(
            new Rect(1100, 20, 320, 660),
            new Rect(0, 0, 1200, 900));

        AssertRect(visibleClip, 0, 0, 100, 660);
    }

    [TestMethod]
    public void CalculateVisibleClip_FullyBelowViewport_ReturnsEmpty()
    {
        Rect visibleClip = ViewMultipleDevicesLayout.CalculateVisibleClip(
            new Rect(0, 650, 320, 100),
            new Rect(0, 0, 1200, 600));

        Assert.IsTrue(visibleClip.IsEmpty);
    }

    [TestMethod]
    public void CalculateVisibleClip_FullyAboveViewport_ReturnsEmpty()
    {
        Rect visibleClip = ViewMultipleDevicesLayout.CalculateVisibleClip(
            new Rect(0, -100, 320, 100),
            new Rect(0, 0, 1200, 600));

        Assert.IsTrue(visibleClip.IsEmpty);
    }

    [TestMethod]
    public void CalculateVisibleClip_ExactBoundary_ReturnsEmpty()
    {
        Rect visibleClip = ViewMultipleDevicesLayout.CalculateVisibleClip(
            new Rect(0, 600, 320, 100),
            new Rect(0, 0, 1200, 600));

        Assert.IsTrue(visibleClip.IsEmpty);
    }

    [TestMethod]
    public void ConvertDipRectToDevicePixels_AtOnePointFiveScale_UsesPixelBounds()
    {
        Rect pixelBounds = ViewMultipleDevicesLayout.ConvertDipRectToDevicePixels(
            new Rect(0, 0, 320, 500),
            1.5,
            1.5);

        AssertRect(pixelBounds, 0, 0, 480, 750);
    }

    [TestMethod]
    public void ConvertDipRectToDevicePixels_NonIntegralBounds_FloorsStartAndCeilsEnd()
    {
        Rect pixelBounds = ViewMultipleDevicesLayout.ConvertDipRectToDevicePixels(
            new Rect(0.2, 0.4, 1, 1),
            1.25,
            1.5);

        AssertRect(pixelBounds, 0, 0, 2, 3);
    }

    private static void AssertRect(
        Rect actual,
        double expectedX,
        double expectedY,
        double expectedWidth,
        double expectedHeight)
    {
        Assert.AreEqual(expectedX, actual.X, 0.000001, "Unexpected X coordinate.");
        Assert.AreEqual(expectedY, actual.Y, 0.000001, "Unexpected Y coordinate.");
        Assert.AreEqual(expectedWidth, actual.Width, 0.000001, "Unexpected width.");
        Assert.AreEqual(expectedHeight, actual.Height, 0.000001, "Unexpected height.");
    }
}
