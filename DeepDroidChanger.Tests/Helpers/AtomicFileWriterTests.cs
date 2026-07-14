using DeepDroidChanger.Helpers;

namespace DeepDroidChanger.Tests.Helpers;

[TestClass]
[DoNotParallelize]
public sealed class AtomicFileWriterTests
{
    [TestMethod]
    public async Task WriteAllTextAsync_PreCanceled_PreservesOriginalAndRemovesTemporaryFile()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "settings.json");
        await File.WriteAllTextAsync(path, "original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            AtomicFileWriter.WriteAllTextAsync(path, "replacement", cancellation.Token));

        Assert.AreEqual("original", await File.ReadAllTextAsync(path));
        Assert.IsFalse(File.Exists(path + ".tmp"));
    }
}
