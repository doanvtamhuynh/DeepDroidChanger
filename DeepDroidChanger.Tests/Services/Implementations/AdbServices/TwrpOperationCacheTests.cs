using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class TwrpOperationCacheTests
{
    [TestMethod]
    public void FreshCache_PreservesPreviousLogs_DoesNotAdoptPreviousSession()
    {
        using var temp = new TestTempDirectory();
        var cache = new TwrpOperationCache(temp.Path);
        var first = new TwrpOperation { ArchivePath = Path.Combine(temp.Path, "name.zip") };
        cache.Create(first);
        string log = Path.Combine(first.CacheDirectory!, "logs", "operation.log");
        File.WriteAllText(log, "previous");
        var next = new TwrpOperation { ArchivePath = first.ArchivePath };
        cache.Create(next);
        Assert.AreNotEqual(first.CacheDirectory, next.CacheDirectory);
        Assert.AreEqual("previous", File.ReadAllText(log));
        Assert.IsFalse(Directory.EnumerateFiles(temp.Path, "*.json", SearchOption.AllDirectories).Any());
        cache.Clean(next, true);
        Assert.IsTrue(Directory.Exists(first.CacheDirectory));
    }

    [TestMethod]
    [DataRow("bad..name")] [DataRow("bad name")] [DataRow("x;reboot")] [DataRow("x\nreboot")]
    public void RemoteArguments_RejectUnsafeNameAndSerial(string input)
    {
        Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpSafety.CliBackupName(input + ".zip"));
        if (input != "bad..name") Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpSafety.ValidateSerial(input));
    }

    [TestMethod]
    [DataRow("[BACKUP COMPLETED IN 3 SECONDS]", false, true)]
    [DataRow("[backup completed in 3.5 seconds]", false, true)]
    [DataRow("[RESTORE COMPLETED]", true, true)]
    [DataRow("[BACKUP COMPLETED IN 3 SECONDS]", true, false)]
    [DataRow("Backup finished?", false, false)]
    public void Completion_RequiresNativeTerminalEvidence(string output, bool restore, bool complete) =>
        Assert.AreEqual(complete, TwrpRecoveryService.HasNativeCompletion(output, restore));

    [TestMethod]
    public void CacheOwnership_RejectsCleanupOutsideRoot()
    {
        using var temp = new TestTempDirectory();
        var cache = new TwrpOperationCache(Path.Combine(temp.Path, "cache"));
        string unrelated = Path.Combine(temp.Path, "other"); Directory.CreateDirectory(unrelated);
        Assert.ThrowsExactly<TwrpWorkflowException>(() => cache.Clean(new TwrpOperation { CacheDirectory = unrelated }, true));
        Assert.IsTrue(Directory.Exists(unrelated));
    }
}
