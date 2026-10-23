using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass, DoNotParallelize]
public sealed class TwrpRecoveryServiceTests
{
    private sealed class InlineRoot : IAdbRootAccessService
    {
        public int Calls { get; private set; }
        public Task ExecuteAsRootAsync(string serial, Func<CancellationToken, Task> action, CancellationToken token) { Calls++; return action(token); }
        public Task<T> ExecuteAsRootAsync<T>(string serial, Func<CancellationToken, Task<T>> action, CancellationToken token) { Calls++; return action(token); }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly TestTempDirectory Temp = new();
        public readonly IAdbCommandService Adb = Substitute.For<IAdbCommandService>();
        public readonly InlineRoot Root = new();
        public readonly List<string> Scripts = [];
        public readonly TwrpSessionStore Store;
        public readonly TwrpRecoveryService Service;
        public string ResultState = "COMPLETED", EngineError = "none";
        public string DeviceMarker = "sargo", ActiveSlot = "_a";
        public bool Recovery, Disconnected, BootRestoreFailed, WrongOperation, EmptyArtifacts, AndroidWriterActive, NeedsOriginal;
        public TwrpSession? Current;
        public Fixture()
        {
            Store = new(Temp.Path);
            Service = new(Adb, Root, new DeviceActionCoordinatorService(), Store, new TwrpImageService(), NullLogger<TwrpRecoveryService>.Instance)
            { PollInterval = TimeSpan.FromMilliseconds(1), RecoveryTimeout = TimeSpan.FromMilliseconds(30) };
            Adb.GetPropertyAsync(Arg.Any<string>(), PropertyConstants.DeepDroidDevice, Arg.Any<CancellationToken>()).Returns(_ => DeviceMarker);
            Adb.RunAdbShellScriptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                string script = call.ArgAt<string>(1); Scripts.Add(script);
                if (Disconnected) throw new IOException("Connection lost");
                if (AndroidWriterActive && script.Contains("A watchdog on this Android boot", StringComparison.Ordinal)) return new CommandResult(1, "", "watchdog still active");
                if (script.Contains("for name in boot_a", StringComparison.Ordinal)) return Ok("""
                    block|boot_a|/dev/block/sda10
                    block|boot_b|/dev/block/sda11
                    block|super|/dev/block/sda15
                    block|userdata|/dev/block/sda20
                    stage|/metadata
                    """ + "\nslot=" + ActiveSlot);
                if (script.Contains("/flash-state'", StringComparison.Ordinal)) return Ok("TWRP_FLASHED");
                if (script == "id -u; getprop ro.twrp.boot") return Ok(Recovery ? "0\n1\n" : "2000\n\n");
                if (script == "id -u") return Ok(Recovery ? "0" : "2000");
                if (script == "getprop ro.twrp.boot") return Ok(Recovery ? "1" : "");
                if (script == "getprop sys.boot_completed") return Ok("1");
                if (script == "getprop ro.twrp.boot; getprop sys.boot_completed") return Ok("\n1\n");
                if (script.Contains("/result'", StringComparison.Ordinal))
                {
                    Current ??= Store.ListAsync("SARGO123", default).GetAwaiter().GetResult().First();
                    return Ok($"op={(WrongOperation ? Guid.Empty : Current.Id):N}\nstate={ResultState}\nerror={EngineError}\nbootError=none\nbootRestored={(BootRestoreFailed ? 0 : 1)}\ndataReadable=1\nsuperSupported=1\nsupported=data;super;\n");
                }
                if (script.Contains("/artifacts'", StringComparison.Ordinal)) return Ok(EmptyArtifacts ? "" : $"data.ext4.win|1024|{TwrpSafety.HashText("data")}\nsuper.emmc.win|2048|{TwrpSafety.HashText("super")}\n");
                if (script.Contains("printf NEEDED", StringComparison.Ordinal)) return Ok(NeedsOriginal ? "NEEDED" : "READY");
                return Ok("");
            });
            Adb.RunAdbAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Ok(""));
            Adb.PushFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Ok(""));
            Adb.PullFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                using var stream = File.Create(call.ArgAt<string>(2)); stream.SetLength(8); return Ok("");
            });
        }
        public TwrpRequest Request(Guid? source = null) => new("SARGO123", ["data", "super"], "/data/media/0", Path.Combine(Temp.Path, "Rescue"), "FLASH TWRP", source);
        public async Task<TwrpSession> PendingAsync(TwrpOperationKind kind)
        {
            var session = TwrpSafetyAndStoreTests.Session(kind); session.State = TwrpSessionState.RecoveryRunning; 
            session.NativeFolder = session.StorageRoot + "/DeepDroidChanger/TWRP/" + session.Id.ToString("N");
            await Store.SaveAsync(session, default);
            using var file = File.Create(Store.FileFor(session.Id, "original.img")); file.SetLength(session.Device.BootBytes);
            Current = session; return session;
        }
        public void Dispose() => Temp.Dispose();
        private static CommandResult Ok(string text) => new(0, text, "");
    }

    [TestMethod]
    public async Task Inspect_RejectsPixel7aBeforeRootOrWrites()
    {
        using var fixture = new Fixture();
        fixture.DeviceMarker = "lynx";
        var result = await fixture.Service.InspectAsync("SARGO123", default);
        CollectionAssert.Contains(result.Issues.ToArray(), "Twrp_UnsupportedDevice"); Assert.AreEqual(0, fixture.Root.Calls);
        await fixture.Adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
        await fixture.Adb.DidNotReceiveWithAnyArgs().PushFileAsync(default!, default!, default!, default);
    }
    [TestMethod]
    public async Task Inspect_UsesOnlyDeepDroidMarkerForDeviceIdentity()
    {
        using var fixture = new Fixture();
        var result = await fixture.Service.InspectAsync("SARGO123", default);
        Assert.IsTrue(result.CanStart, string.Join(",", result.Issues));
        Assert.AreEqual("sargo", result.Device!.Codename);
        Assert.AreEqual("", result.Device.Fingerprint);
        Assert.AreEqual(1, fixture.Root.Calls);
        await fixture.Adb.Received(1).GetPropertyAsync("SARGO123", PropertyConstants.DeepDroidDevice, Arg.Any<CancellationToken>());
        await fixture.Adb.DidNotReceive().GetPropertyAsync(Arg.Any<string>(), Arg.Is<string>(property => property != PropertyConstants.DeepDroidDevice), Arg.Any<CancellationToken>());
        Assert.IsFalse(fixture.Scripts.Any(script => script.Contains("ro.product", StringComparison.Ordinal) || script.Contains("ro.build", StringComparison.Ordinal)
            || script.Contains("device-tree", StringComparison.Ordinal) || script.Contains("/proc/cmdline", StringComparison.Ordinal)
            || script.Contains("update_engine", StringComparison.Ordinal) || script.Contains("verifiedboot", StringComparison.Ordinal)));
        await fixture.Adb.DidNotReceiveWithAnyArgs().PushFileAsync(default!, default!, default!, default);
    }
    [TestMethod]
    [DataRow("")] [DataRow("lynx")] [DataRow("starlte")] [DataRow("SARGO")]
    public async Task Inspect_RejectsMissingOrUnsupportedMarkerBeforeRoot(string marker)
    {
        using var fixture = new Fixture { DeviceMarker = marker };
        var result = await fixture.Service.InspectAsync("SARGO123", default);
        Assert.IsFalse(result.CanStart);
        CollectionAssert.Contains(result.Issues.ToArray(), "Twrp_UnsupportedDevice");
        Assert.AreEqual(0, fixture.Root.Calls);
    }
    [TestMethod]
    public async Task Inspect_RejectsUnknownBootSlot()
    {
        using var fixture = new Fixture { ActiveSlot = "" };
        var result = await fixture.Service.InspectAsync("SARGO123", default);
        CollectionAssert.Contains(result.Issues.ToArray(), "Twrp_InvalidSlot");
    }
    [TestMethod]
    public async Task Run_BackupWithoutQualificationUsesOnlyAdbAndPreservesCommandOrder()
    {
        using var fixture = new Fixture();
        var session = await fixture.Service.RunAsync(fixture.Request(), null, default);
        Assert.AreEqual(TwrpSessionState.Completed, session.State, session.WorkflowError); Assert.IsTrue(session.BootRestored); Assert.AreEqual(2, session.Artifacts.Length);
        Assert.IsTrue(File.Exists(Path.Combine(session.RescueDirectory, "DeepDroidTwrp-" + session.Id.ToString("N"), "original.img")));
        Assert.IsFalse(fixture.Scripts.Any(script => script.Contains("sha256sum", StringComparison.Ordinal) || script.Contains("blockdev", StringComparison.Ordinal)));
        int original = fixture.Scripts.FindIndex(script => script.Contains("dd if=", StringComparison.Ordinal));
        int journal = fixture.Scripts.FindIndex(script => script.Contains("/session.json'", StringComparison.Ordinal));
        int flash = fixture.Scripts.FindIndex(script => script.Contains("nohup sh", StringComparison.Ordinal)); Assert.IsTrue(original >= 0 && journal > original && flash > journal);
        await fixture.Adb.Received(1).RunAdbAsync("SARGO123", "reboot recovery", Arg.Any<CancellationToken>());
        await fixture.Adb.DidNotReceiveWithAnyArgs().RunFastbootAsync(default!, default);
    }
    [TestMethod]
    [DataRow("FAILED", "native_operation_failed", TwrpOperationKind.Backup, TwrpSessionState.Failed)]
    [DataRow("FAILED", "data_not_decrypted_or_empty", TwrpOperationKind.Backup, TwrpSessionState.Failed)]
    [DataRow("NEEDS_RECOVERY", "native_operation_failed", TwrpOperationKind.Restore, TwrpSessionState.NeedsRecovery)]
    public async Task Resume_EngineFailureNeverReplaysNativeOperation(string result, string error, TwrpOperationKind kind, TwrpSessionState expected)
    {
        using var fixture = new Fixture { ResultState = result, EngineError = error, Recovery = kind == TwrpOperationKind.Restore };
        var pending = await fixture.PendingAsync(kind);
        var session = await fixture.Service.RecoverAsync(pending.Id, null, default);
        Assert.AreEqual(expected, session.State); Assert.IsTrue(session.BootRestored); Assert.AreEqual(error, session.WorkflowError);
        await fixture.Adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
        await fixture.Adb.DidNotReceiveWithAnyArgs().PushFileAsync(default!, default!, default!, default);
    }
    [TestMethod]
    public async Task Resume_RestoreSuccessCompletesWithoutReplayingIt()
    {
        using var fixture = new Fixture(); var pending = await fixture.PendingAsync(TwrpOperationKind.Restore);
        var session = await fixture.Service.RecoverAsync(pending.Id, null, default);
        Assert.AreEqual(TwrpSessionState.Completed, session.State); Assert.IsTrue(session.BootRestored);
        Assert.IsFalse(fixture.Scripts.Any(script => script.Contains("dd if=", StringComparison.Ordinal)));
    }
    [TestMethod]
    public async Task Resume_BootRestoreFailureStaysInRecoveryWithoutReboot()
    {
        using var fixture = new Fixture { BootRestoreFailed = true, Recovery = true }; var pending = await fixture.PendingAsync(TwrpOperationKind.Restore);
        var session = await fixture.Service.RecoverAsync(pending.Id, null, default);
        Assert.AreEqual(TwrpSessionState.NeedsRecovery, session.State); Assert.IsFalse(session.BootRestored);
        await fixture.Adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
    }
    [TestMethod]
    public async Task Resume_RejectsResultFromAnotherOperation()
    {
        using var fixture = new Fixture { WrongOperation = true }; var pending = await fixture.PendingAsync(TwrpOperationKind.Backup);
        var error = await Assert.ThrowsExactlyAsync<TwrpWorkflowException>(() => fixture.Service.RecoverAsync(pending.Id, null, default));
        Assert.AreEqual("Twrp_InvalidJournal", error.MessageKey);
        await fixture.Adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
    }
    [TestMethod]
    public async Task Resume_DisconnectedDevicePreservesRescueAndPendingJournal()
    {
        using var fixture = new Fixture(); var pending = await fixture.PendingAsync(TwrpOperationKind.Restore); fixture.Disconnected = true;
        var session = await fixture.Service.RecoverAsync(pending.Id, null, default);
        Assert.AreEqual(TwrpSessionState.NeedsRecovery, session.State); Assert.IsFalse(session.BootRestored);
        Assert.IsTrue(File.Exists(fixture.Store.FileFor(session.Id, "original.img")));
    }
    [TestMethod]
    public async Task Run_TimeoutAttemptsBootRollbackAndKeepsRestorePending()
    {
        using var fixture = new Fixture { ResultState = "RECOVERY_RUNNING" };
        var session = await fixture.Service.RunAsync(fixture.Request(), null, default);
        Assert.AreEqual(TwrpSessionState.Failed, session.State, session.BootRestoreError); Assert.IsTrue(session.BootRestored);
        Assert.AreEqual("Twrp_RecoveryTimeout", session.WorkflowError);
        Assert.IsTrue(fixture.Scripts.Any(script => script.Contains("dd if=", StringComparison.Ordinal) && script.Contains("ro.deepdroid.device", StringComparison.Ordinal)));
    }
    [TestMethod]
    public async Task Run_CancellationAfterFlashIntentCannotInterruptCriticalCommands()
    {
        using var fixture = new Fixture(); using var cancellation = new CancellationTokenSource();
        var progress = new SynchronousProgress(value => { if (value.State == TwrpSessionState.FlashIntent) cancellation.Cancel(); });
        var session = await fixture.Service.RunAsync(fixture.Request(), progress, cancellation.Token);
        Assert.AreEqual(TwrpSessionState.Completed, session.State, session.WorkflowError); Assert.IsTrue(session.BootRestored);
    }
    [TestMethod]
    public async Task Run_AndroidWatchdogOwnershipPreventsCompetingBootWriter()
    {
        using var fixture = new Fixture { ResultState = "RECOVERY_RUNNING", AndroidWriterActive = true };
        var session = await fixture.Service.RunAsync(fixture.Request(), null, default);
        Assert.AreEqual(TwrpSessionState.NeedsRecovery, session.State); Assert.IsFalse(session.BootRestored);
        Assert.IsFalse(fixture.Scripts.Any(script => script.Contains("dd if=", StringComparison.Ordinal) && script.Contains("ro.deepdroid.device", StringComparison.Ordinal)));
        Assert.IsTrue(File.Exists(fixture.Store.FileFor(session.Id, "original.img")));
    }
    [TestMethod]
    public async Task Run_MissingPayloadCannotProduceSuccessfulBackup()
    {
        using var fixture = new Fixture { EmptyArtifacts = true };
        var session = await fixture.Service.RunAsync(fixture.Request(), null, default);
        Assert.AreEqual(TwrpSessionState.Failed, session.State); Assert.IsTrue(session.BootRestored);
        Assert.AreEqual("Twrp_InvalidManifest", session.WorkflowError);
    }
    [TestMethod]
    public async Task Run_RestoreUsesMatchingCompletedNativeBackup()
    {
        using var fixture = new Fixture();
        var source = TwrpSafetyAndStoreTests.Session(); source.Device = (await fixture.Service.InspectAsync("SARGO123", default)).Device!;
        source.State = TwrpSessionState.Completed; source.BootRestored = true;
        source.NativeFolder = source.StorageRoot + "/DeepDroidChanger/TWRP/" + source.Id.ToString("N");
        source.Artifacts = [new("data.ext4.win", 1024, TwrpSafety.HashText("data")), new("super.emmc.win", 2048, TwrpSafety.HashText("super"))];
        await fixture.Store.SaveAsync(source, default);
        var session = await fixture.Service.RunAsync(fixture.Request(source.Id), null, default);
        Assert.AreEqual(TwrpSessionState.Completed, session.State, session.WorkflowError);
        Assert.AreEqual(TwrpOperationKind.Restore, session.Kind); Assert.AreEqual(source.NativeFolder, session.NativeFolder);
        await fixture.Adb.DidNotReceive().PushFileAsync("SARGO123", Arg.Is<string>(path => path.EndsWith("source.sha256")), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Delete_BlocksAnUnfinishedRescueSession()
    {
        using var fixture = new Fixture(); var pending = await fixture.PendingAsync(TwrpOperationKind.Restore);
        await Assert.ThrowsExactlyAsync<TwrpWorkflowException>(() => fixture.Service.DeleteAsync("SARGO123", pending.Id, default));
        Assert.IsTrue(File.Exists(fixture.Store.FileFor(pending.Id, "original.img")));
    }
    [TestMethod]
    public async Task Resume_SuppliesOriginalFromPcToRecoveryRamWithoutHashOrSizeCommands()
    {
        using var fixture = new Fixture { Recovery = true, NeedsOriginal = true, ResultState = "NEEDS_RECOVERY" };
        var pending = await fixture.PendingAsync(TwrpOperationKind.Backup);
        var session = await fixture.Service.RecoverAsync(pending.Id, null, default);
        Assert.AreEqual(TwrpSessionState.NeedsRecovery, session.State);
        await fixture.Adb.Received().PushFileAsync("SARGO123", Arg.Is<string>(path => path.EndsWith("original.img")),
            Arg.Is<string>(path => path.EndsWith("pc-original.img.part")), Arg.Any<CancellationToken>());
        Assert.IsTrue(fixture.Scripts.Any(script => script.StartsWith("mv ", StringComparison.Ordinal) && script.Contains("pc-original.img.part", StringComparison.Ordinal)));
        Assert.IsFalse(fixture.Scripts.Any(script => script.Contains("sha256sum", StringComparison.Ordinal) || script.Contains("blockdev", StringComparison.Ordinal)));
        await fixture.Adb.DidNotReceiveWithAnyArgs().RunFastbootAsync(default!, default);
    }
    private sealed class SynchronousProgress(Action<TwrpProgress> report) : IProgress<TwrpProgress> { public void Report(TwrpProgress value) => report(value); }
}
