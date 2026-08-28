using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ViewDeviceWindowServiceTests
{
    [TestMethod]
    [DataRow("Samsung S9", "22e20c84e40c7ece", "Samsung S9 - 22e20c84e40c7ece")]
    [DataRow(null, "22e20c84e40c7ece", "22e20c84e40c7ece")]
    [DataRow("   ", "22e20c84e40c7ece", "22e20c84e40c7ece")]
    [DataRow("22E20C84E40C7ECE", "22e20c84e40c7ece", "22e20c84e40c7ece")]
    public void FormatWindowTitle_UsesNameAndSerialWithoutDuplicateFallback(
        string? displayName,
        string serial,
        string expected)
    {
        string title = ViewDeviceWindowService.FormatWindowTitle(serial, displayName);

        Assert.AreEqual(expected, title);
    }
}
