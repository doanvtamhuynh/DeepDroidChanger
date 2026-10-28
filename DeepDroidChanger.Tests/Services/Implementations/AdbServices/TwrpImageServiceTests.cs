using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using DeepDroidChanger.Tests.Helpers;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class TwrpImageServiceTests
{
    [TestMethod]
    [DataRow("sargo", "twrp_sargo.img")]
    [DataRow("tissot", "twrp_tissot_treble.img")]
    public async Task RecoverySource_RejectsUnpinnedImageWithoutMutatingIt(string device, string filename)
    {
        using var temp = new TestTempDirectory();
        string path = Path.Combine(temp.Path, filename);
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var service = new TwrpImageService(temp.Path);
        Assert.AreEqual(path, service.GetDefaultImagePath(device));
        await Assert.ThrowsExactlyAsync<TwrpWorkflowException>(() => service.OpenBootSourceAsync(device, default));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
        Assert.AreEqual(1, Directory.GetFiles(temp.Path).Length);
    }
}
