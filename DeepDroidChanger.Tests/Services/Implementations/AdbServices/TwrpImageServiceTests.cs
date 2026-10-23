using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass, DoNotParallelize]
public sealed class TwrpImageServiceTests
{
    [TestMethod]
    public async Task Build_PreservesSourceKernelDtbAndUnrelatedCpioAndRehashesAvb()
    {
        using var temp = new TestTempDirectory(); var service = new TwrpImageService();
        string sourcePath = Path.Combine(AppContext.BaseDirectory, "Assets", "TWRP", "twrp_sargo.img");
        byte[] source = await File.ReadAllBytesAsync(sourcePath);
        string sourceHash = Convert.ToHexStringLower(SHA256.HashData(source)); Assert.AreEqual(TwrpSafety.SourceHash, sourceHash);
        string destination = Path.Combine(temp.Path, "guarded.img");
        string hash = await service.BuildAsync(TwrpSafetyAndStoreTests.Session(), destination, default);
        byte[] output = await File.ReadAllBytesAsync(destination);
        Assert.AreEqual(64 * 1024 * 1024, output.Length); Assert.AreEqual(hash, Convert.ToHexStringLower(SHA256.HashData(output)));
        Assert.AreNotEqual(sourceHash, hash); TwrpImageService.VerifyGenerated(source, output);
        var before = TwrpImageService.ReadRamdisk(source); var after = TwrpImageService.ReadRamdisk(output);
        var edited = new HashSet<string> { "init.recovery.service.rc", "twres/portrait.xml", "twres/ui.xml", "system/etc/twrp.flags" };
        foreach (var entry in before.Where(item => !edited.Contains(item.Name)))
        {
            var unchanged = after.Single(item => item.Name == entry.Name);
            CollectionAssert.AreEqual(entry.Data, unchanged.Data, entry.Name); CollectionAssert.AreEqual(entry.Fields, unchanged.Fields, entry.Name);
        }
        string guard = Encoding.UTF8.GetString(after.Single(item => item.Name == "system/bin/deepdroid-guard.sh").Data);
        StringAssert.Contains(guard, "pc-original.img"); StringAssert.Contains(guard, "cli run"); StringAssert.Contains(guard, "set ddc_function nandroid");
        Assert.IsFalse(guard.Contains("cli errorprobe", StringComparison.Ordinal));
        Assert.IsFalse(guard.Contains("sha256sum", StringComparison.Ordinal)); Assert.IsFalse(guard.Contains("blockdev", StringComparison.Ordinal));
        StringAssert.Contains(guard, "dd if=\"$WORK/original.img\" of=\"$BOOT\"");
        Assert.IsFalse(guard.Contains("dd if=\"$BACKUP", StringComparison.Ordinal));
        string bridge = Encoding.UTF8.GetString(after.Single(item => item.Name == "twres/portrait.xml").Data);
        var page = XDocument.Parse(bridge).Descendants("page").Single(item => (string?)item.Attribute("name") == "singleaction_page");
        Assert.AreEqual(2, page.Descendants("action").Count(item => (string?)item.Attribute("function") == "%ddc_pending_function%"));
        StringAssert.Contains(bridge, "%tw_operation_status% %tw_cancel_backup%");
        var layout = TwrpImageService.Parse(output); output[layout.Page + 20] ^= 1;
        Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpImageService.VerifyGenerated(source, output));
        output[layout.Page + 20] ^= 1; output[layout.VbmetaOffset + 430] ^= 1;
        Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpImageService.VerifyGenerated(source, output));
        Assert.AreEqual(sourceHash, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))));
    }
    [TestMethod]
    public async Task Build_RejectsImageLargerThanBootPartition()
    {
        using var temp = new TestTempDirectory(); var session = TwrpSafetyAndStoreTests.Session(); session.Device = session.Device with { BootBytes = 16 * 1024 * 1024 };
        var error = await Assert.ThrowsExactlyAsync<TwrpWorkflowException>(() => new TwrpImageService().BuildAsync(session, Path.Combine(temp.Path, "image"), default));
        Assert.AreEqual("Twrp_ImageTooLarge", error.MessageKey);
    }
    [TestMethod]
    public async Task Source_RejectsUnpinnedImageWithoutMutatingIt()
    {
        using var temp = new TestTempDirectory(); string path = Path.Combine(temp.Path, "twrp_sargo.img"); await File.WriteAllBytesAsync(path, [1, 2, 3]);
        await Assert.ThrowsExactlyAsync<TwrpWorkflowException>(() => new TwrpImageService(temp.Path).VerifySourceAsync(default));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
    }
    [TestMethod]
    public void Parser_RejectsTruncatedAndInvalidHeader()
    {
        foreach (byte[] bytes in new[] { Array.Empty<byte>(), new byte[4096], Encoding.ASCII.GetBytes("ANDROID!") })
            Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpImageService.Parse(bytes));
        Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpImageService.PatchBridge("<recovery><page name='singleaction_page'/></recovery>"));
    }
}
