using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class DeviceViewerDiagnosticBufferTests
{
    [TestMethod]
    public void Buffer_RetainsOnlyTheConfiguredNumberOfNewestLines()
    {
        var buffer = new DeviceViewerDiagnosticBuffer(3);

        buffer.Add("one");
        buffer.Add("two");
        buffer.Add("three");
        buffer.Add("four");

        CollectionAssert.AreEqual(new[] { "two", "three", "four" }, buffer.Snapshot());
        Assert.AreEqual(3, buffer.Count);
    }

    [TestMethod]
    public void Buffer_IgnoresBlankDiagnostics()
    {
        var buffer = new DeviceViewerDiagnosticBuffer(2);

        buffer.Add(" ");
        buffer.Add("diagnostic");

        Assert.AreEqual(1, buffer.Count);
        Assert.IsFalse(buffer.IsEmpty);
    }
}
