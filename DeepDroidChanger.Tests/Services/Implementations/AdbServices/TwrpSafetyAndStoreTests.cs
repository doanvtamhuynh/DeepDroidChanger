using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class TwrpSafetyAndStoreTests
{
    internal static TwrpDeviceInfo Device => new("SARGO123", "sargo", "google/sargo:13/build", "google/vendor:13/build", 33,
        "_a", "/dev/block/sda10", 64 * 1024 * 1024, TwrpSafety.HashText("layout"), "google,sargo",
        1024 * 1024, [new("data", "/data", "userdata", 1024 * 1024), new("super", "/super", "system_a", 1024 * 1024)], ["/metadata"]);
    internal static TwrpSession Session(TwrpOperationKind kind = TwrpOperationKind.Backup) => new()
    {
        Device = Device, Kind = kind, OriginalHash = TwrpSafety.HashText("original"), RuntimeImageHash = TwrpSafety.HashText("runtime"),
        StagingRoot = "/metadata", StorageRoot = "/data/media/0", NativeFolder = "/data/media/0/DeepDroidChanger/TWRP/backup", Partitions = ["data", "super"]
    };
    [TestMethod]
    [DataRow("", false)] [DataRow("_a", true)] [DataRow("_b", true)] [DataRow("a", false)] [DataRow("_c", false)] [DataRow("_a;reboot", false)]
    public void Slot_RestrictsWritesToKnownBootSlots(string slot, bool valid)
    {
        if (valid) Assert.AreEqual("/dev/block/by-name/boot" + slot, TwrpSafety.Boot(slot));
        else Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpSafety.Boot(slot));
    }
    [TestMethod]
    [DataRow("/data/media/0")] [DataRow("/usb_otg")] [DataRow("/mnt/media_rw/1234-5678")]
    public void Storage_AcceptsWhitelistedRoots(string path) => Assert.AreEqual(path, TwrpSafety.Storage(path));
    [TestMethod]
    [DataRow("/")] [DataRow("/data")] [DataRow("/data/media/0/../../boot")] [DataRow("/data/media/0;reboot")] [DataRow("/storage//bad")] [DataRow("/storage/card\nreboot")]
    public void Storage_RejectsTraversalAndShellInput(string path) => Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpSafety.Storage(path));
    [TestMethod]
    public void Selection_ExcludesBootLogicalPartitionsAndDuplicates()
    {
        foreach (string[] parts in new[] { Array.Empty<string>(), new[] { "boot" }, ["data", "data"], ["super", "system"], ["vbmeta"] })
            Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpSafety.ValidateSelection(Device, parts));
        TwrpSafety.ValidateSelection(Device, ["data", "super"]);
    }
    [TestMethod]
    public void RestoreCompatibility_ChangesInvalidateBackup()
    {
        Assert.IsTrue(TwrpSafety.Compatible(Device, Device));
        foreach (var changed in new[] { Device with { Serial = "OTHER" }, Device with { Fingerprint = "changed" }, Device with { VendorFingerprint = "changed" },
            Device with { AndroidSdk = 34 }, Device with { Slot = "_b" }, Device with { LayoutHash = "changed" }, Device with { Hardware = "other" } })
        {
            Assert.IsFalse(TwrpSafety.Compatible(Device, changed));
        }
    }
    [TestMethod]
    public void Transitions_RequireRestoredBootBeforeCompletion()
    {
        var session = Session();
        Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpSafety.Transition(session, TwrpSessionState.Completed));
        foreach (var state in new[] { TwrpSessionState.OriginalSaved, TwrpSessionState.ImagePrepared, TwrpSessionState.FlashIntent,
            TwrpSessionState.TwrpFlashed, TwrpSessionState.Rebooted, TwrpSessionState.RecoveryRunning, TwrpSessionState.RestoreBootIntent,
            TwrpSessionState.BootRestored, TwrpSessionState.Completed })
        {
            if (state == TwrpSessionState.BootRestored)
            {
                Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpSafety.Transition(session, state));
                session.BootRestored = true;
            }
            TwrpSafety.Transition(session, state);
        }
        Assert.IsTrue(session.IsTerminal);
        Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpSafety.Transition(session, TwrpSessionState.FlashIntent));
    }
    [TestMethod]
    public async Task Journal_RoundTripsAndPersistsPendingSessionAcrossRestart()
    {
        using var temp = new TestTempDirectory();
        var store = new TwrpSessionStore(temp.Path); var session = Session(); session.State = TwrpSessionState.FlashIntent;
        await store.SaveAsync(session, default);
        var reopened = new TwrpSessionStore(temp.Path);
        var loaded = await reopened.LoadAsync(session.Id, default);
        Assert.AreEqual(session.OriginalHash, loaded.OriginalHash); Assert.AreEqual(TwrpSafety.SourceHash, loaded.SourceImageHash);
        Assert.AreEqual(TwrpSessionState.FlashIntent, loaded.State); Assert.IsFalse(loaded.IsTerminal);
        Assert.AreEqual(1, (await reopened.ListAsync("", default)).Count);
        Assert.AreEqual(0, (await reopened.ListAsync("OTHER", default)).Count);
    }
    [TestMethod]
    public async Task Journal_CorruptionBlocksListingInsteadOfHidingPendingWork()
    {
        using var temp = new TestTempDirectory(); var store = new TwrpSessionStore(temp.Path); var session = Session();
        await store.SaveAsync(session, default);
        await File.WriteAllTextAsync(store.FileFor(session.Id, "session.json"), "{broken");
        var error = await Assert.ThrowsExactlyAsync<TwrpWorkflowException>(() => store.ListAsync(Device.Serial, default));
        Assert.AreEqual("Twrp_InvalidJournal", error.MessageKey);
        Assert.IsTrue(File.Exists(store.FileFor(session.Id, "session.json")));
    }
    [TestMethod]
    [DataRow("../outside")] [DataRow("C:\\outside")] [DataRow("..")] [DataRow("")]
    public void Journal_FilenamesCannotEscapeOperation(string name)
    {
        using var temp = new TestTempDirectory(); var store = new TwrpSessionStore(temp.Path);
        Assert.ThrowsExactly<ArgumentException>(() => store.FileFor(Guid.NewGuid(), name));
    }
    [TestMethod]
    public void Manifest_RejectsTraversalDuplicateAndInvalidMetadata()
    {
        string hash = TwrpSafety.HashText("payload");
        Assert.AreEqual(8L, TwrpRecoveryService.ParseArtifacts($"data.ext4.win|8|{hash}\n").Single().Bytes);
        foreach (string text in new[] { $"../outside|8|{hash}", $"data.win|8|{hash}\ndata.win|8|{hash}", $"data.win|-1|{hash}", "" })
            Assert.ThrowsExactly<TwrpWorkflowException>(() => TwrpRecoveryService.ParseArtifacts(text));
    }
    [TestMethod]
    public void FlashAndRescue_OnlyTargetCurrentBootAndNeverAutomaticallyRebootFromPc()
    {
        var session = Session(); string script = TwrpScripts.Flash(session), rescue = TwrpScripts.RescuePowerShell(session);
        Assert.IsFalse(script.Contains('\r'), "Android helper must have LF line endings even when C# sources use CRLF.");
        Assert.IsTrue(script.IndexOf("FLASH_INTENT", StringComparison.Ordinal) < script.IndexOf("dd if=\"$REMOTE/recovery.img", StringComparison.Ordinal));
        StringAssert.Contains(script, "sleep 60"); StringAssert.Contains(script, "rollback"); StringAssert.Contains(script, "BOOT='/dev/block/by-name/boot_a'");
        StringAssert.Contains(rescue, "dd if="); StringAssert.Contains(rescue, "boot_a"); Assert.IsFalse(rescue.Contains("fastboot", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("sha256sum", StringComparison.Ordinal)); Assert.IsFalse(script.Contains("blockdev", StringComparison.Ordinal));
        Assert.IsFalse(rescue.Contains("sha256sum", StringComparison.Ordinal));
        Assert.IsFalse(rescue.Contains(" flashing unlock", StringComparison.Ordinal));
        string config = TwrpScripts.Configuration(session); StringAssert.Contains(config, "SELECTION='/data;/super;'");
        Assert.IsFalse(config.Contains("SELECTION='/boot", StringComparison.Ordinal));
    }
}
