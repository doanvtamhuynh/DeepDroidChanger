using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class FileSystemServiceTests
{
    [TestMethod]
    public void FileExists_ValidatesInputAndPhysicalFile()
    {
        var service = new FileSystemService();
        string path = Path.GetTempFileName();
        try
        {
            Assert.IsTrue(service.FileExists(path));
            Assert.IsFalse(service.FileExists(path + ".missing"));
            Assert.IsFalse(service.FileExists(string.Empty));
            Assert.IsFalse(service.FileExists("   "));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
