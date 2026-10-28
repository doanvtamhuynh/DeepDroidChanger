using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass, DoNotParallelize]
public sealed class TwrpRecoveryServiceTests
{
    private sealed class InlineProgress(Action<TwrpProgress> action) : IProgress<TwrpProgress>
    {
        public void Report(TwrpProgress value) => action(value);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly TestTempDirectory Temp = new();
        public readonly IAdbCommandService Adb = Substitute.For<IAdbCommandService>();
        public readonly DeviceActionCoordinatorService Coordinator = new();
        public readonly TwrpOperationCache Cache;
        public readonly TwrpRecoveryService Service;
        public readonly List<string> Scripts = [];
        public readonly List<TwrpProgress> Progress = [];
        public Func<string, Action<ProcessUpdate>, CancellationToken, Task<CommandResult>> Native;
        public Func<string, string, CancellationToken, Task<CommandResult>>? Pull;
        public bool Recovery = true, RemoteLock, RejectOwnership, FailDownload, Truncate, MissingChunk, FailCleanup;
        public int NativeCalls, RemovedPayloads, Reboots;
        public string? LastRemoteFolder;
        public string LanguageXml = "";
        public Fixture()
        {
            Cache = new(Path.Combine(Temp.Path, "Backup", "cache"));
            Service = new(Adb, Coordinator, Cache, new TwrpImageService(), NullLogger<TwrpRecoveryService>.Instance)
            { PollInterval = TimeSpan.FromMilliseconds(1), RecoveryTimeout = TimeSpan.FromSeconds(5), EntryRecoveryTimeout = TimeSpan.FromMilliseconds(20) };
            Native = (command, update, token) =>
            {
                string text = command.StartsWith("twrp restore", StringComparison.Ordinal) ? "[RESTORE COMPLETED IN 2 SECONDS]" : "[BACKUP COMPLETED IN 2 SECONDS]";
                update(new(ProcessUpdateKind.Started, 42));
                update(new(ProcessUpdateKind.StandardOutput, 42, "Data 37%\n"));
                update(new(ProcessUpdateKind.StandardError, 42, "native diagnostic\n"));
                update(new(ProcessUpdateKind.StandardOutput, 42, text));
                update(new(ProcessUpdateKind.Exited, 42, ExitCode: 0));
                return Task.FromResult(new CommandResult(0, text, ""));
            };
            Adb.RunAdbShellStreamingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<ProcessUpdate>>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                NativeCalls++;
                return Native(call.ArgAt<string>(1), call.ArgAt<Action<ProcessUpdate>>(2), call.ArgAt<CancellationToken>(3));
            });
            Adb.RunAdbAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                string command = call.ArgAt<string>(1);
                if (command == "get-state") return Ok(Recovery ? "recovery\n" : "device\n");
                if (command == "reboot recovery") Recovery = true;
                if (command == "reboot") Reboots++;
                return Ok("");
            });
            Adb.RunAdbShellScriptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                string script = call.ArgAt<string>(1);
                Scripts.Add(script);
                if (script.Contains("/twres/languages", StringComparison.Ordinal)) return Ok(LanguageXml);
                if (script.Contains("mkdir /tmp/deepdroid-native-active", StringComparison.Ordinal))
                {
                    if (RemoteLock || RejectOwnership) return new CommandResult(1, "", "native active");
                    RemoteLock = true;
                }
                if (script.Contains("rmdir /tmp/deepdroid-native-active", StringComparison.Ordinal)) RemoteLock = false;
                if (script.Contains("rm -rf", StringComparison.Ordinal))
                {
                    if (FailCleanup) return new CommandResult(1, "", "cleanup denied");
                    RemovedPayloads++;
                }
                if (script.Contains("wc -c", StringComparison.Ordinal)) return Ok("data.ext4.win000|8\ndata.ext4.win001|8\nrecovery.log|3\n");
                return Ok("");
            });
            Adb.PullFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                string remote = call.ArgAt<string>(1), local = call.ArgAt<string>(2);
                LastRemoteFolder = remote;
                if (Pull is not null) return await Pull(remote, local, call.ArgAt<CancellationToken>(3));
                string folder = Path.Combine(local, remote.Split('/')[^1]);
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(Path.Combine(folder, "data.ext4.win000"), Truncate ? "half" : "12345678");
                if (!MissingChunk) await File.WriteAllTextAsync(Path.Combine(folder, "data.ext4.win001"), "abcdefgh");
                await File.WriteAllTextAsync(Path.Combine(folder, "recovery.log"), "log");
                return FailDownload ? new CommandResult(1, "", "disconnect") : Ok("");
            });
            Adb.PushFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Ok(""));
        }
        public TwrpRequest Request(string name = "backup", string? source = null, string serial = "UI123") =>
            new(serial, TwrpSafety.Confirmation,
                BackupName: name, BackupDirectory: Path.Combine(Temp.Path, "Output"), SourceArchivePath: source, RecoveryDevice: "starlte");
        public Task<TwrpOperation> Run(TwrpRequest? request = null, Action<TwrpProgress>? onProgress = null, CancellationToken token = default) =>
            Service.RunAsync(request ?? Request(), new InlineProgress(value => { Progress.Add(value); onProgress?.Invoke(value); }), token);
        public void OnlyLogs(TwrpOperation operation)
        {
            string cache = operation.CacheDirectory!;
            CollectionAssert.AreEquivalent(new[] { "logs" }, Directory.GetFileSystemEntries(cache).Select(Path.GetFileName).ToArray());
            CollectionAssert.AreEquivalent(new[] { "operation.log", "process.log", "recovery.log" }, Directory.GetFiles(Path.Combine(cache, "logs")).Select(Path.GetFileName).ToArray());
            Assert.IsFalse(Directory.EnumerateFiles(Temp.Path, "*.partial", SearchOption.AllDirectories).Any());
            Assert.IsFalse(Directory.EnumerateFiles(Temp.Path, "*.json", SearchOption.AllDirectories).Any());
        }
        public void Dispose() => Temp.Dispose();
        public static CommandResult Ok(string output) => new(0, output, "");
    }

    [TestMethod]
    public async Task Backup_UsesCapturedSerial_OnlyRecoveryPreflight_Streams_Verifies_Publishes_Cleans()
    {
        using var f = new Fixture();
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Completed, operation.State, operation.WorkflowError + (Directory.Exists(operation.CacheDirectory) ? File.ReadAllText(Path.Combine(operation.CacheDirectory!, "logs", "operation.log")) : ""));
        Assert.IsTrue(File.Exists(operation.ArchivePath));
        Assert.IsFalse(Directory.Exists(operation.CacheDirectory));
        Assert.AreEqual(1, f.NativeCalls);
        Assert.AreEqual(1, f.RemovedPayloads);
        Assert.AreEqual(1, f.Reboots);
        Assert.IsFalse(f.RemoteLock);
        Assert.IsFalse(f.Scripts.Any(script => script.Contains("test ! -e \"$path\"", StringComparison.Ordinal)
            && script.Contains("mkdir -p", StringComparison.Ordinal)), "Backup must leave directory creation to native TWRP.");
        Assert.AreEqual("/data/media/0/TWRP/BACKUPS/UI123/backup", f.LastRemoteFolder);
        await f.Adb.Received(1).RunAdbAsync("UI123", "get-state", Arg.Any<CancellationToken>());
        await f.Adb.DidNotReceiveWithAnyArgs().GetPropertyAsync(default!, default!, default);
        await f.Adb.DidNotReceiveWithAnyArgs().RunFastbootAsync(default!, default);
        await f.Adb.Received(1).RunAdbShellStreamingAsync("UI123", "twrp backup D 'backup'", Arg.Any<Action<ProcessUpdate>>(), Arg.Any<CancellationToken>());
        Assert.IsTrue(f.Progress.Any(p => p.Percent == 37));
        Assert.IsTrue(f.Progress.Any(p => p.Detail?.Contains("native diagnostic", StringComparison.Ordinal) == true));
        Assert.IsTrue(f.Progress.Any(p => p.State == TwrpOperationState.Verifying));
        Assert.IsFalse(f.Scripts.Any(s => new[] { "getprop", "boot_id", "decrypt", "mount ", "df ", "blockdev", "guard", "runner", "session.json" }.Any(word => s.Contains(word, StringComparison.Ordinal))));
        Assert.IsFalse(Directory.Exists(Path.Combine(f.Temp.Path, "Backup", "TWRP")));
        Assert.IsFalse(Directory.EnumerateFiles(f.Temp.Path, "entry*.json", SearchOption.AllDirectories).Any());
        var manifest = await new TwrpArchiveService().ReadAsync(operation.ArchivePath!, default);
        Assert.IsTrue(manifest.Payload.All(p => p.Sha256?.Length == 64));
    }

    [TestMethod]
    public async Task NotRecovery_UsesSelectedInstalledRecovery_ThenChecksRecovery()
    {
        using var f = new Fixture { Recovery = false };
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Completed, operation.State, operation.WorkflowError + (Directory.Exists(operation.CacheDirectory) ? File.ReadAllText(Path.Combine(operation.CacheDirectory!, "logs", "operation.log")) : ""));
        await f.Adb.Received(1).RunAdbAsync("UI123", "reboot recovery", Arg.Any<CancellationToken>());
        await f.Adb.Received(2).RunAdbAsync("UI123", "get-state", Arg.Any<CancellationToken>());
        await f.Adb.DidNotReceiveWithAnyArgs().GetPropertyAsync(default!, default!, default);
    }

    [TestMethod]
    [DataRow("sargo", "twrp_sargo.img")]
    [DataRow("tissot", "twrp_tissot_treble.img")]
    public async Task Entry_UsesPreviouslySelectedVerifiedImage_AndAlwaysTargetsCapturedSerial(string device, string image)
    {
        using var f = new Fixture { Recovery = false };
        f.Adb.RunFastbootAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => { f.Recovery = true; return Fixture.Ok(""); });
        var operation = await f.Run(f.Request() with { RecoveryDevice = device });
        Assert.AreEqual(TwrpOperationState.Completed, operation.State, operation.WorkflowError);
        await f.Adb.Received(1).RunAdbAsync("UI123", "reboot bootloader", Arg.Any<CancellationToken>());
        await f.Adb.Received(1).RunFastbootAsync(Arg.Is<string>(args => args.StartsWith("-s \"UI123\" boot \"", StringComparison.Ordinal) && args.EndsWith(image + "\"", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await f.Adb.DidNotReceiveWithAnyArgs().GetPropertyAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task Entry_RecoveryNeverReady_ReturnsClearErrorBeforeNative()
    {
        using var f = new Fixture { Recovery = false };
        f.Adb.RunAdbAsync("UI123", "reboot recovery", Arg.Any<CancellationToken>()).Returns(Fixture.Ok(""));
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Failed, operation.State);
        Assert.AreEqual("Twrp_EntryRecoveryWaitFailed", operation.WorkflowError);
        Assert.AreEqual(0, f.NativeCalls);
        f.OnlyLogs(operation);
    }

    [TestMethod]
    public async Task Completion_AcceptsRecoveryLanguageTemplate_AfterNativeExit()
    {
        using var f = new Fixture();
        f.LanguageXml = "<?xml version=\"1.0\"?><language><string name=\"backup_completed\">[SAUVEGARDE TERMINEE EN {1} SECONDES]</string></language>";
        f.Native = (_, _, _) => Task.FromResult(Fixture.Ok("[SAUVEGARDE TERMINEE EN 12 SECONDES]"));
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Completed, operation.State, operation.WorkflowError);
        Assert.AreEqual(1, f.NativeCalls);
    }

    [TestMethod]
    public async Task AdbUnavailable_FailsBeforeNative_OnlyLogs()
    {
        using var f = new Fixture();
        f.Adb.RunAdbAsync("UI123", "get-state", Arg.Any<CancellationToken>()).Returns(new CommandResult(1, "", "offline"));
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Failed, operation.State);
        Assert.AreEqual(0, f.NativeCalls);
        Assert.AreEqual("Twrp_EntryInspectFailed", operation.WorkflowError);
        f.OnlyLogs(operation);
    }

    [TestMethod]
    [DataRow(1, "[BACKUP FAILED]", false)]
    [DataRow(0, "", true)]
    [DataRow(0, "E:Unable to read data\n[BACKUP COMPLETED IN 2 SECONDS]", false)]
    [DataRow(0, "[RESTORE COMPLETED IN 2 SECONDS]", true)]
    [DataRow(1, "[RESTORE FAILED]", true)]
    [DataRow(1, "adb: device offline", true)]
    public async Task NativeFailure_OrUncertainCompletion_NeverDownloadsOrReplays(int exit, string output, bool unknown)
    {
        using var f = new Fixture();
        f.Native = (_, _, _) => Task.FromResult(new CommandResult(exit, output, ""));
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Failed, operation.State);
        Assert.AreEqual(unknown, operation.NativeStateUnknown);
        Assert.AreEqual(1, f.NativeCalls);
        Assert.AreEqual(unknown ? 0 : 1, f.RemovedPayloads);
        await f.Adb.DidNotReceiveWithAnyArgs().PullFileAsync(default!, default!, default!, default);
        Assert.AreEqual(0, f.Reboots);
        f.OnlyLogs(operation);
        if (unknown) await Assert.ThrowsExactlyAsync<TwrpWorkflowException>(() => f.Run(f.Request("fresh")));
    }

    [TestMethod]
    public async Task NativeProgress_ParsesSplitChunks_WithoutMixingStdoutAndStderr()
    {
        using var f = new Fixture();
        var native = f.Native;
        f.Native = async (command, update, token) =>
        {
            update(new(ProcessUpdateKind.StandardOutput, 42, "4"));
            update(new(ProcessUpdateKind.StandardError, 42, "5%"));
            update(new(ProcessUpdateKind.StandardOutput, 42, "0%"));
            return await native(command, update, token);
        };
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Completed, operation.State);
        Assert.IsTrue(f.Progress.Any(p => p.Percent == 40));
        Assert.IsFalse(f.Progress.Any(p => p.Percent == 45));
    }

    [TestMethod]
    public async Task SilentNative_RemainsRunning_EmitsElapsedWithoutFakePercentage()
    {
        using var f = new Fixture();
        var native = f.Native;
        f.Native = async (command, update, token) =>
        {
            update(new(ProcessUpdateKind.Started, 42));
            await Task.Delay(1150, token);
            return await native(command, update, token);
        };
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Completed, operation.State, operation.WorkflowError + (Directory.Exists(operation.CacheDirectory) ? File.ReadAllText(Path.Combine(operation.CacheDirectory!, "logs", "operation.log")) : ""));
        Assert.IsTrue(f.Progress.Any(p => p.MessageKey == "Twrp_NativeStatus" && p.Percent is null && p.Elapsed >= TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task NativeTimeout_PreservesRemote_BlocksNextRun_OnlyLogs()
    {
        using var f = new Fixture();
        f.Service.RecoveryTimeout = TimeSpan.FromMilliseconds(30);
        f.Native = async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return Fixture.Ok(""); };
        var operation = await f.Run();
        Assert.IsTrue(operation.NativeStateUnknown);
        Assert.AreEqual(0, f.RemovedPayloads);
        Assert.IsTrue(File.ReadAllText(Path.Combine(operation.CacheDirectory!, "logs", "operation.log")).Contains("timeout", StringComparison.Ordinal));
        f.OnlyLogs(operation);
    }

    [TestMethod]
    [DataRow(true, false, false)] [DataRow(false, true, false)] [DataRow(false, false, true)]
    public async Task IncompleteDownload_FailsBeforeZip_CleansOnlyOwnPayload(bool failure, bool truncated, bool missing)
    {
        using var f = new Fixture { FailDownload = failure, Truncate = truncated, MissingChunk = missing };
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Failed, operation.State);
        Assert.IsFalse(File.Exists(operation.ArchivePath));
        Assert.AreEqual(1, f.RemovedPayloads);
        Assert.IsFalse(f.Progress.Any(p => p.State == TwrpOperationState.Packaging));
        f.OnlyLogs(operation);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ZipFailure_OrVerifyFailure_DoesNotPublish_OnlyLogs(bool verify)
    {
        using var f = new Fixture();
        var operation = await f.Run(onProgress: value =>
        {
            if (!verify && value.State == TwrpOperationState.Packaging) Directory.CreateDirectory(Path.Combine(f.Temp.Path, "Output", "backup.zip"));
            if (verify && value.State == TwrpOperationState.Verifying)
                File.WriteAllBytes(Directory.GetFiles(Path.Combine(f.Temp.Path, "Output"), "*.partial").Single(), [1, 2, 3]);
        });
        Assert.AreEqual(TwrpOperationState.Failed, operation.State);
        Assert.IsFalse(File.Exists(operation.ArchivePath));
        f.OnlyLogs(operation);
    }

    [TestMethod]
    public async Task StopNative_CancelsPc_PreservesRemote_Warns_BlocksOverlap()
    {
        using var f = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Native = async (_, update, token) =>
        {
            update(new(ProcessUpdateKind.Started, 42));
            update(new(ProcessUpdateKind.StandardOutput, 42, "working"));
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Fixture.Ok("");
        };
        Task<TwrpOperation> running = f.Run();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var current = f.Coordinator.GetOperation("UI123")!;
        var busy = await Assert.ThrowsExactlyAsync<TwrpWorkflowException>(() => f.Run(f.Request("other")));
        Assert.AreEqual("Twrp_DeviceBusy", busy.MessageKey);
        Assert.IsTrue(await f.Service.RequestStopAsync("UI123", current.OperationId, default));
        var operation = await running;
        Assert.AreEqual(TwrpOperationState.Stopped, operation.State);
        Assert.IsTrue(operation.NativeStateUnknown);
        Assert.AreEqual("Twrp_DirectResultUnknown", operation.WorkflowError);
        Assert.AreEqual(0, f.RemovedPayloads);
        Assert.AreEqual(0, f.Reboots);
        Assert.IsTrue(f.RemoteLock);
        f.OnlyLogs(operation);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task StopDownload_OrPackaging_OnlyLogs_AndRetryIsFresh(bool packaging)
    {
        using var f = new Fixture();
        using var stop = new CancellationTokenSource();
        var operation = await f.Run(onProgress: value =>
        {
            if (value.State == (packaging ? TwrpOperationState.Packaging : TwrpOperationState.Downloading)) stop.Cancel();
        }, token: stop.Token);
        Assert.AreEqual(TwrpOperationState.Stopped, operation.State);
        f.OnlyLogs(operation);
        string oldLog = File.ReadAllText(Path.Combine(operation.CacheDirectory!, "logs", "operation.log"));
        var next = await f.Run();
        Assert.AreEqual(TwrpOperationState.Completed, next.State);
        Assert.AreNotEqual(operation.Id, next.Id);
        Assert.AreEqual(2, f.NativeCalls);
        Assert.AreEqual(oldLog, File.ReadAllText(Path.Combine(operation.CacheDirectory!, "logs", "operation.log")));
    }

    [TestMethod]
    public async Task StopDuringDownload_ClosesTransferBeforeCleaningPartialFiles()
    {
        using var f = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pull = async (remote, local, token) =>
        {
            Directory.CreateDirectory(local);
            await using (var partial = File.Create(Path.Combine(local, "download.partial")))
            {
                await partial.WriteAsync(new byte[] { 1, 2, 3 }, token);
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            return Fixture.Ok("");
        };
        var task = f.Run();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var current = f.Coordinator.GetOperation("UI123")!;
        Assert.IsTrue(await f.Service.RequestStopAsync("UI123", current.OperationId, default));
        var operation = await task;
        Assert.AreEqual(TwrpOperationState.Stopped, operation.State);
        Assert.IsFalse(operation.NativeStateUnknown);
        f.OnlyLogs(operation);
    }

    [TestMethod]
    public async Task RemoteOwnershipRefused_NeverRunsNativeOrDeletesAnotherOperation()
    {
        using var f = new Fixture { RejectOwnership = true };
        var operation = await f.Run();
        Assert.AreEqual(TwrpOperationState.Failed, operation.State);
        Assert.AreEqual(0, f.NativeCalls);
        Assert.AreEqual(0, f.RemovedPayloads);
        f.OnlyLogs(operation);
    }

    [TestMethod]
    public async Task LockedTemporaryFile_ReportsCleanupFailureWithoutClaimingSuccess()
    {
        using var f = new Fixture();
        FileStream? held = null;
        try
        {
            f.Pull = async (_, local, _) =>
            {
                Directory.CreateDirectory(local);
                string path = Path.Combine(local, "locked.partial");
                await File.WriteAllTextAsync(path, "partial");
                held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return new CommandResult(1, "", "download failed");
            };
            var operation = await f.Run();
            Assert.AreEqual("Twrp_CleanupFailed", operation.WorkflowError);
            string log = File.ReadAllText(Path.Combine(operation.CacheDirectory!, "logs", "operation.log"));
            StringAssert.Contains(log, "PC cleanup FAILED");
            Assert.IsFalse(log.Contains("PC cleanup completed", StringComparison.Ordinal));
        }
        finally { held?.Dispose(); }
    }

    [TestMethod]
    public async Task CleanupFailure_IsLoggedWithoutClaimingSuccess()
    {
        using var f = new Fixture { FailDownload = true, FailCleanup = true };
        var operation = await f.Run();
        string log = File.ReadAllText(Path.Combine(operation.CacheDirectory!, "logs", "operation.log"));
        StringAssert.Contains(log, "Remote cleanup failed");
        Assert.IsFalse(log.Contains("Remote cleanup completed", StringComparison.Ordinal));
        Assert.IsTrue(f.RemoteLock);
        f.OnlyLogs(operation);
    }

    [TestMethod]
    [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)]
    public async Task Restore_AcceptsOldValidDataZips_WithoutLiveIdentityChecks(int version)
    {
        using var f = new Fixture();
        string zip = CreateArchive(f.Temp.Path, version);
        var operation = await f.Run(f.Request(source: zip));
        Assert.AreEqual(TwrpOperationState.Completed, operation.State, operation.WorkflowError + (Directory.Exists(operation.CacheDirectory) ? File.ReadAllText(Path.Combine(operation.CacheDirectory!, "logs", "operation.log")) : ""));
        await f.Adb.Received(1).RunAdbShellStreamingAsync("UI123", Arg.Is<string>(cmd => cmd.StartsWith("twrp restore '/data/media/0/TWRP/BACKUPS/UI123/restore_", StringComparison.Ordinal) && cmd.EndsWith("' D", StringComparison.Ordinal)), Arg.Any<Action<ProcessUpdate>>(), Arg.Any<CancellationToken>());
        await f.Adb.DidNotReceiveWithAnyArgs().GetPropertyAsync(default!, default!, default);
        Assert.IsFalse(Directory.Exists(operation.CacheDirectory));
        Assert.IsTrue(File.Exists(zip));
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Restore_FailureOrStop_OnlyLogs_NoReboot(bool stop)
    {
        using var f = new Fixture();
        string zip = CreateArchive(f.Temp.Path, 6);
        using var cancellation = new CancellationTokenSource();
        f.Native = (_, _, _) =>
        {
            if (stop) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            return Task.FromResult(new CommandResult(1, "[RESTORE FAILED]", ""));
        };
        var operation = await f.Run(f.Request(source: zip), token: cancellation.Token);
        Assert.AreEqual(stop ? TwrpOperationState.Stopped : TwrpOperationState.Failed, operation.State);
        Assert.AreEqual(0, f.Reboots);
        Assert.AreEqual(stop ? 0 : 1, f.RemovedPayloads);
        f.OnlyLogs(operation);
    }

    internal static string CreateArchive(string directory, int version, bool badHash = false)
    {
        string path = Path.Combine(directory, "old.zip");
        var payload = new List<TwrpArchivePayload>();
        var files = new Dictionary<string, byte[]> { ["old/data.ext4.win"] = Encoding.UTF8.GetBytes("payload"), ["old/recovery.log"] = Encoding.UTF8.GetBytes("log") };
        if (version is 3 or 4) files.Add("original.img", [1, 2, 3]);
        if (version is 3 or 4) payload.Add(new("original.img", 3, null));
        payload.Add(new("old/data.ext4.win", 7, "data", badHash ? new string('0', 64) : null));
        payload.Add(new("old/recovery.log", 3, null));
        var device = new TwrpDeviceInfo("ARCHIVED_SERIAL", "sargo", "", "", 0, "_a", "", 0, "layout", "", 0, [new("data", "/data", "", 0)], []);
        Guid id = Guid.NewGuid(); var created = DateTimeOffset.UtcNow;
        object manifest = version switch
        {
            6 => new TwrpDataBackupManifest(6, id, "old", created, "sargo", "ARCHIVED_SERIAL", "sargo", ["data"], payload.ToArray()),
            4 or 5 => new TwrpCliBackupManifest(version, id, "old", created, "sargo", "ARCHIVED_SERIAL", "_a", "layout", "sargo", ["data"], payload.ToArray()),
            _ => new TwrpBackupManifest(version, id, "old", created, device, new(), ["data"], payload.ToArray())
        };
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var json = zip.CreateEntry("backup.json").Open()) JsonSerializer.Serialize(json, manifest, TwrpArchiveService.JsonOptions);
        if (version == 3)
        {
            using var type = zip.CreateEntry("type.json").Open();
            JsonSerializer.Serialize(type, new TwrpBackupType(1, id, "old", created, "sargo", "ARCHIVED_SERIAL", "_a", "sargo", ["data"]), TwrpArchiveService.JsonOptions);
        }
        foreach (var file in files)
        {
            using var entry = zip.CreateEntry(file.Key).Open(); entry.Write(file.Value);
        }
        return path;
    }

    [TestMethod]
    public async Task Restore_InvalidZipOrPayloadHash_FailsBeforeRecoveryOrNative()
    {
        using var f = new Fixture();
        string zip = CreateArchive(f.Temp.Path, 6, badHash: true);
        var operation = await f.Run(f.Request(source: zip));
        Assert.AreEqual(TwrpOperationState.Failed, operation.State);
        Assert.AreEqual(0, f.NativeCalls);
        await f.Adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
        f.OnlyLogs(operation);
    }
}
