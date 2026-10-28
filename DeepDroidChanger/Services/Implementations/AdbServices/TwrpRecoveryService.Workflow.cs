using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private async Task CollectResultAsync(TwrpSession session, IProgress<TwrpProgress>? progress, Func<bool>? stopRequested = null, CancellationToken exportToken = default)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + RecoveryTimeout;
        bool guardReadyReported = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (stopRequested?.Invoke() == true)
            {
                session.StopRequested = true;
                try
                {
                    // Only the init-owned guard handles interruption; never cancel its ADB collector or boot writer.
                    await ShellAsync(session.Device.Serial, $"if [ \"$(getprop ro.twrp.boot)\" = 1 ] && [ -d {TwrpSafety.Quote(TwrpScripts.Work(session))} ]; then printf '%s' {TwrpSafety.Quote(session.Id.ToString("N"))} > {TwrpSafety.Quote(TwrpScripts.Work(session) + "/stop-request")}; fi", CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e) { logger.LogDebug("Stop pending for {Operation}: {FailureType}.", session.Id, e.GetType().Name); }
            }
            string? text = await ReadResultAsync(session).ConfigureAwait(false);
            if (text is not null)
            {
                var values = ParseFields(text);
                if (GetField(values, "op") != session.Id.ToString("N")) throw new TwrpWorkflowException("Twrp_InvalidJournal");
                string state = GetField(values, "state");
                if (session.CliBackup && state == "BACKUP_READY")
                {
                    if (session.State == TwrpSessionState.Rebooted) await StateAsync(session, TwrpSessionState.RecoveryRunning, progress, CancellationToken.None).ConfigureAwait(false);
                    await AcceptCliPayloadAsync(session, progress, exportToken).ConfigureAwait(false);
                }
                if (state == "GUARD_READY" && !guardReadyReported)
                {
                    progress?.Report(new(session.Id, session.State, "Twrp_GuardReady"));
                    guardReadyReported = true;
                }
                if (state is "COMPLETED" or "FAILED" or "NEEDS_RECOVERY")
                {
                    if (state == "COMPLETED" && values.ContainsKey("selected"))
                    {
                        string[] effective = GetField(values, "selected").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (effective.Distinct().Count() != effective.Length || effective.Any(id => id == "media" || id == "boot" && !session.NativeBoot || !session.Partitions.Contains(id)))
                            throw new TwrpWorkflowException("Twrp_InvalidJournal");
                        if (session.Kind == TwrpOperationKind.Backup && !session.NativeBoot && session.Partitions.Contains("boot")) effective = [.. effective, "boot"];
                        if (effective.Length == 0) throw new TwrpWorkflowException("Twrp_NoSelectedPartitions");
                        session.SkippedPartitions = session.SkippedPartitions.Concat(session.Partitions.Except(effective)).Distinct().ToArray();
                        session.Partitions = session.Partitions.Where(effective.Contains).ToArray();
                        ReportSkipped(session, progress);
                    }
                    session.RecoveryLog = text;
                    string workflowError = GetField(values, "error");
                    session.WorkflowError = workflowError == "none" ? null : workflowError;
                    if (session.CliBackup && session.WorkflowError is "payload_pull_failed" or "payload_pull_timeout")
                    {
                        await ReadArtifactsAsync(session).ConfigureAwait(false);
                        session.NativeBackupCompleted = true;
                    }
                    string bootRestoreError = GetField(values, "bootError");
                    session.BootRestoreError = bootRestoreError is "none" or "" ? null : bootRestoreError;
                    session.BootRestored = GetField(values, "bootRestored") == "1" || GetField(values, "bootVerified") == "1";
                    if (!session.BootRestored) { session.State = TwrpSessionState.NeedsRecovery; await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false); return; }
                    if (session.Kind == TwrpOperationKind.Backup && state == "COMPLETED") await ReadArtifactsAsync(session).ConfigureAwait(false);
                    await CaptureLogsAsync(session).ConfigureAwait(false);
                    if (state == "NEEDS_RECOVERY") { session.State = TwrpSessionState.NeedsRecovery; await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false); return; }
                    await ShellAsync(session.Device.Serial, $"if [ \"$(getprop ro.twrp.boot)\" = 1 ]; then touch {TwrpSafety.Quote(TwrpScripts.Work(session) + "/host-ack")}; fi", CancellationToken.None).ConfigureAwait(false);
                    await AwaitAndroidReturnAsync(session).ConfigureAwait(false);
                    if (session.State == TwrpSessionState.Rebooted) await StateAsync(session, TwrpSessionState.RecoveryRunning, progress, CancellationToken.None).ConfigureAwait(false);
                    if (session.State != TwrpSessionState.RecoveryRunning) await StateAsync(session, TwrpSessionState.RestoreBootIntent, progress, CancellationToken.None).ConfigureAwait(false);
                    await StateAsync(session, TwrpSessionState.BootRestored, progress, CancellationToken.None).ConfigureAwait(false);
                    await StateAsync(session, state == "COMPLETED" ? TwrpSessionState.Completed : TwrpSessionState.Failed, progress, CancellationToken.None).ConfigureAwait(false);
                    return;
                }
                if (state == "RECOVERY_RUNNING" && session.State == TwrpSessionState.Rebooted)
                    await StateAsync(session, TwrpSessionState.RecoveryRunning, progress, CancellationToken.None).ConfigureAwait(false);
            }
            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
        throw new TwrpWorkflowException("Twrp_RecoveryTimeout");
    }
    private static void ReportSkipped(TwrpSession session, IProgress<TwrpProgress>? progress)
    {
        if (session.SkippedPartitions.Length > 0)
            progress?.Report(new(session.Id, session.State, "Twrp_PartitionsSkipped", Detail: string.Join(", ", session.SkippedPartitions)));
    }
    private async Task<string?> ReadResultAsync(TwrpSession session)
    {
        try
        {
            var result = await ShellAsync(session.Device.Serial, "id -u; getprop ro.twrp.boot", CancellationToken.None).ConfigureAwait(false);
            bool recovery = result.StandardOutput.Split('\n').Any(line => line.Trim() == "1");
            string command = $"cat {TwrpSafety.Quote(TwrpScripts.Work(session) + "/result")} 2>/dev/null || cat {TwrpSafety.Quote(TwrpScripts.Remote(session) + "/result")} 2>/dev/null || true";
            if (recovery)
            {
                await SupplyOriginalToRecoveryAsync(session).ConfigureAwait(false);
                return (await ShellAsync(session.Device.Serial, command, CancellationToken.None).ConfigureAwait(false)).StandardOutput is { Length: > 0 } text ? text : null;
            }
            if ((await ShellAsync(session.Device.Serial, "getprop sys.boot_completed", CancellationToken.None).ConfigureAwait(false)).StandardOutput.Trim() != "1") return null;
            return await root.ExecuteAsRootAsync(session.Device.Serial, async ct =>
            {
                string text = (await ShellAsync(session.Device.Serial, command, ct).ConfigureAwait(false)).StandardOutput;
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug("Waiting for TWRP result for {Operation}: {FailureType}.", session.Id, exception.GetType().Name);
            return null;
        }
    }
    private async Task SupplyOriginalToRecoveryAsync(TwrpSession session)
    {
        string work = TwrpScripts.Work(session);
        string original = work + "/original.img", delivered = work + "/pc-original.img";
        var ready = await ShellAsync(session.Device.Serial,
            $"if [ -f {TwrpSafety.Quote(original)} ] || [ -f {TwrpSafety.Quote(delivered)} ]; then printf READY; else printf NEEDED; fi", CancellationToken.None).ConfigureAwait(false);
        if (ready.StandardOutput.Trim() != "NEEDED") return;
        await ShellAsync(session.Device.Serial, $"mkdir -p {TwrpSafety.Quote(work)}", CancellationToken.None).ConfigureAwait(false);
        // Separate incoming filename: never overwrite the guard's RAM copy or race
        // its copy from staging. The guard only consumes a completed transfer.
        await TransferAsync(session.Device.Serial, store.FileFor(session.Id, "original.img"), delivered + ".part", true, CancellationToken.None).ConfigureAwait(false);
        await ShellAsync(session.Device.Serial, $"mv {TwrpSafety.Quote(delivered + ".part")} {TwrpSafety.Quote(delivered)}", CancellationToken.None).ConfigureAwait(false);
    }
    private async Task AwaitAndroidReturnAsync(TwrpSession session)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                string android = (await ShellAsync(session.Device.Serial, "getprop sys.boot_completed", CancellationToken.None).ConfigureAwait(false)).StandardOutput.Trim();
                string recovery = (await ShellAsync(session.Device.Serial, "getprop ro.twrp.boot", CancellationToken.None).ConfigureAwait(false)).StandardOutput.Trim();
                if (android == "1" && recovery != "1")
                {
                    return;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogDebug("Waiting for Android boot: {FailureType}.", e.GetType().Name); }
            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
        session.BootRestored = false;
        throw new TwrpWorkflowException("Twrp_BootRestoreFailed");
    }
    private async Task ReadArtifactsAsync(TwrpSession session)
    {
        string? text = await ReadRemoteTextAsync(session, "artifacts").ConfigureAwait(false);
        if (text is null) throw new TwrpWorkflowException("Twrp_InvalidManifest");
        session.Artifacts = string.IsNullOrWhiteSpace(text) && !session.NativeBoot && session.Partitions.SequenceEqual(new[] { "boot" }) ? [] : ParseArtifacts(text);
        if (session.Kind == TwrpOperationKind.Backup && !session.NativeBoot && session.Partitions.Contains("boot"))
            session.Artifacts = [.. session.Artifacts, OriginalBootArtifact(session)];
        ValidatePayload(session.Device, session.Partitions, session.Artifacts);
    }
    private static void ValidatePayload(TwrpDeviceInfo device, IReadOnlyList<string> partitions, TwrpArtifact[] artifacts)
    {
        TwrpSafety.ValidateRequestedSelection(partitions);
        foreach (string id in partitions)
        {
            string prefix = device.Partitions.Single(part => part.Id == id).MountPoint.TrimStart('/') + ".";
            if (!artifacts.Any(file => file.Name.StartsWith(prefix, StringComparison.Ordinal) && file.Name.Contains(".win", StringComparison.Ordinal) && !file.Name.EndsWith(".sha2", StringComparison.Ordinal) && !file.Name.EndsWith(".md5", StringComparison.Ordinal)))
                throw new TwrpWorkflowException("Twrp_InvalidManifest");
        }
    }
    internal static TwrpArtifact[] ParseArtifacts(string text)
    {
        var result = new List<TwrpArtifact>();
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = line.Split('|');
            if (fields.Length != 3 || fields[0] is "." or ".." || !TwrpSafety.SafeName().IsMatch(fields[0]) || fields[0].Length > 255
                || !long.TryParse(fields[1], out long bytes) || bytes < 0) throw new TwrpWorkflowException("Twrp_InvalidManifest");
            result.Add(new(fields[0], bytes));
        }
        if (result.Count == 0 || result.Count > 10000 || result.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new TwrpWorkflowException("Twrp_InvalidManifest");
        return result.ToArray();
    }
    private async Task<string?> ReadRemoteTextAsync(TwrpSession session, string name)
    {
        string script = $"cat {TwrpSafety.Quote(TwrpScripts.Work(session) + "/" + name)} 2>/dev/null || cat {TwrpSafety.Quote(TwrpScripts.Remote(session) + "/" + name)} 2>/dev/null || true";
        string identity = (await ShellAsync(session.Device.Serial, "id -u", CancellationToken.None).ConfigureAwait(false)).StandardOutput.Trim();
        return identity == "0" ? (await ShellAsync(session.Device.Serial, script, CancellationToken.None).ConfigureAwait(false)).StandardOutput
            : await root.ExecuteAsRootAsync(session.Device.Serial, async ct => (await ShellAsync(session.Device.Serial, script, ct).ConfigureAwait(false)).StandardOutput, CancellationToken.None).ConfigureAwait(false);
    }
    private async Task CaptureLogsAsync(TwrpSession session)
    {
        foreach (string name in new[] { "guard.log", "recovery.log" })
        {
            try
            {
                string? text = await ReadRemoteTextAsync(session, name).ConfigureAwait(false);
                if (text is not null) await File.WriteAllTextAsync(store.FileFor(session.Id, name), text, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning("Unable to retrieve {LogName} for {Operation}: {FailureType}.", name, session.Id, e.GetType().Name); }
        }
    }
}
