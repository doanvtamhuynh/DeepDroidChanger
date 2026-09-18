using DeepDroidChanger.Views;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class ViewMultipleDevicesLayoutTests
{
    [TestMethod]
    public void CalculateStreamHeight_UsesPortraitAspectRatio()
    {
        double height = ViewMultipleDevicesLayout.CalculateStreamHeight(320, 1080d / 2220d);

        Assert.AreEqual(657.777, height, 0.001);
    }

    [TestMethod]
    public void CalculateStreamHeight_UsesLandscapeAspectRatio()
    {
        double height = ViewMultipleDevicesLayout.CalculateStreamHeight(320, 2220d / 1080d);

        Assert.AreEqual(155.675, height, 0.001);
    }

    [TestMethod]
    public void CalculateStreamHeight_UsesFallbackForInvalidAspectRatio()
    {
        double height = ViewMultipleDevicesLayout.CalculateStreamHeight(320, 0);

        Assert.AreEqual(320 * (16d / 9d), height, 0.001);
    }

    [TestMethod]
    public void CalculateStreamHeight_ReturnsZeroForInvalidWidth()
    {
        Assert.AreEqual(0, ViewMultipleDevicesLayout.CalculateStreamHeight(0, 9d / 16d));
        Assert.AreEqual(0, ViewMultipleDevicesLayout.CalculateStreamHeight(double.NaN, 9d / 16d));
        Assert.AreEqual(0, ViewMultipleDevicesLayout.CalculateStreamHeight(double.PositiveInfinity, 9d / 16d));
    }

    [TestMethod]
    public void CalculateStreamHeight_ReturnsZeroForNonFiniteResult()
    {
        Assert.AreEqual(
            0,
            ViewMultipleDevicesLayout.CalculateStreamHeight(
                double.MaxValue,
                double.Epsilon));
    }
}
