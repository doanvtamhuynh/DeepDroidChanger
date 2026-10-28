using System.IO;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    // Compatibility for external-recovery journals created by the old RAM runner.
    // New sessions use direct native commands and never launch this runner.
    private async Task InspectDataRecoveryAsync(TwrpSession session, CancellationToken token)
    {
        var result = await ShellAsync(session.Device.Serial, """
            set -eu
            printf 'uid=%s\nrecovery=%s\n' "$(id -u)" "$(getprop ro.twrp.boot)"
            printf 'bootId=%s\n' "$(cat /proc/sys/kernel/random/boot_id)"
            for bin in /sbin /system/bin; do
                if [ -x "$bin/sh" ] && [ -x "$bin/twrp" ]; then printf 'shell=%s/sh\ntwrp=%s/twrp\n' "$bin" "$bin"; break; fi
            done
            """, token).ConfigureAwait(false);
        var fields = ParseFields(result.StandardOutput);
        string bootId = GetField(fields, "bootId"), shell = GetField(fields, "shell"), twrp = GetField(fields, "twrp");
        if (GetField(fields, "uid") != "0" || GetField(fields, "recovery") != "1"
            || !Guid.TryParse(bootId, out _) || shell is not ("/sbin/sh" or "/system/bin/sh")
            || twrp is not ("/sbin/twrp" or "/system/bin/twrp")) throw new TwrpWorkflowException("Twrp_DataPreflightFailed");
        if (session.RecoveryBootId.Length > 0 && session.RecoveryBootId != bootId)
            throw new TwrpWorkflowException("Twrp_DataRecoveryChanged");
        session.RecoveryBootId = bootId;
    }

    private async Task WaitRunnerDataResultAsync(TwrpSession session, Guid operationId,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + RecoveryTimeout;
        bool disconnected = false;
        bool resuming = true;
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (_confirmedStops.ContainsKey(operationId))
            {
                session.StopRequested = true;
                await TrySignalDataStopAsync(session).ConfigureAwait(false);
            }
            string? text = null;
            try
            {
                if (disconnected) await InspectDataRecoveryAsync(session, token).ConfigureAwait(false);
                text = (await ShellAsync(session.Device.Serial,
                    $"cat {TwrpSafety.Quote(TwrpScripts.Work(session) + "/result")} 2>/dev/null || true", token).ConfigureAwait(false)).StandardOutput;
                disconnected = false;
            }
            catch (TwrpWorkflowException e) when (e.MessageKey is "Twrp_DataRecoveryChanged" or "Twrp_DataPreflightFailed") { throw; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { disconnected = true; logger.LogDebug(exception, "Waiting for Data runner {Operation}.", session.Id); }
            if (!string.IsNullOrWhiteSpace(text))
            {
                var fields = ParseFields(text);
                if (GetField(fields, "op") != session.Id.ToString("N") || GetField(fields, "bootId") != session.RecoveryBootId)
                    throw new TwrpWorkflowException("Twrp_InvalidJournal");
                string state = GetField(fields, "state");
                if (resuming && state is not ("COMPLETED" or "FAILED" or "NEEDS_RECOVERY"))
                {
                    await CheckDataRunnerAliveAsync(session, token).ConfigureAwait(false);
                    if (state is "PREPARING" or "WAITING_DATA" or "READY")
                    {
                        string permission = (await ShellAsync(session.Device.Serial,
                            $"cat {TwrpSafety.Quote(TwrpScripts.Work(session) + "/start")} 2>/dev/null || true", token).ConfigureAwait(false)).StandardOutput.Trim();
                        // Journal intent alone never authorizes replay. A prepared
                        // runner without its original start permission is abandoned.
                        if (permission != session.Id.ToString("N")) await TrySignalDataStopAsync(session).ConfigureAwait(false);
                    }
                    resuming = false;
                }
                if (state == "WAITING_DATA") ReportData(session, progress, "Twrp_DataUnlock");
                if (state == "COMPLETED")
                {
                    session.NativeCompleted = true;
                    session.NativeBackupCompleted = session.CliBackup;
                    session.WorkflowError = null;
                    await DataStateAsync(session, TwrpSessionState.NativeCompleted, progress, token).ConfigureAwait(false);
                    return;
                }
                if (state is "FAILED" or "NEEDS_RECOVERY")
                {
                    session.WorkflowError = GetField(fields, "error") switch
                    {
                        "user_stopped" => "Twrp_Stopped", "data_locked" => "Twrp_DataUnlockTimeout",
                        "insufficient_space" => "Twrp_DataInsufficientSpace", "device_busy" => "Twrp_DeviceBusy",
                        "cli_unavailable" or "setup_failed" => "Twrp_DataPreflightFailed",
                        "backup_already_exists" => "Twrp_DataBackupExists",
                        "native_still_running" => "Twrp_DataNativeActive", _ => "Twrp_DataNativeFailed"
                    };
                    session.RecoveryLog = text;
                    session.State = state == "FAILED" ? TwrpSessionState.Failed : TwrpSessionState.NeedsRecovery;
                    await CaptureDataLogsAsync(session, token).ConfigureAwait(false);
                    await store.SaveAsync(session, token).ConfigureAwait(false);
                    ReportData(session, progress, session.WorkflowError);
                    return;
                }
            }
            else if (resuming && !disconnected) throw new TwrpWorkflowException("Twrp_DataDetached");
            await Task.Delay(PollInterval, token).ConfigureAwait(false);
        }
        throw new TwrpWorkflowException("Twrp_RecoveryTimeout");
    }

    private async Task FinishDataAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckCliCache(session);
        if (!session.NativeCompleted) throw new TwrpWorkflowException("Twrp_PendingSession");
        if (!session.CleanupCompleted)
        {
            await InspectDataRecoveryAsync(session, token).ConfigureAwait(false);
            await CaptureDataLogsAsync(session, token).ConfigureAwait(false);
            if (session.CliBackup)
            {
                if (!session.PayloadDownloaded)
                {
                    var text = await ShellAsync(session.Device.Serial, $"cat {TwrpSafety.Quote(TwrpScripts.Work(session) + "/artifacts")}", token).ConfigureAwait(false);
                    session.Artifacts = ParseArtifacts(text.StandardOutput);
                    TwrpArchiveService.ValidateDataArtifacts(session.Artifacts);
                    ReportData(session, progress, "Twrp_Downloading");
                    string folder = Path.Combine(CliPayloadDirectory(session), TwrpSafety.CliBackupName(session.ArchivePath!));
                    TwrpArchiveService.CheckDirectory(folder);
                    Directory.CreateDirectory(folder);
                    session.ExportState = TwrpExportState.Downloading;
                    await store.SaveAsync(session, token).ConfigureAwait(false);
                    foreach (var file in session.Artifacts)
                    {
                        string local = Path.Combine(folder, file.Name);
                        string partial = local + ".partial";
                        if (File.Exists(partial) && File.GetAttributes(partial).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
                        await PayloadTransferAsync(session.Device.Serial, session.NativeFolder + "/" + file.Name, partial, false, token).ConfigureAwait(false);
                        if (new FileInfo(partial).Length != file.Bytes) throw new TwrpWorkflowException("Twrp_TransferFailed");
                        File.Move(partial, local, overwrite: true);
                    }
                    session.PayloadDownloaded = true;
                    await store.SaveAsync(session, token).ConfigureAwait(false);
                }
                await PackageDataBackupAsync(session, progress, token).ConfigureAwait(false);
            }
            await CleanupDataPayloadAsync(session, token).ConfigureAwait(false);
            session.CleanupCompleted = true;
            session.ExportState = session.CliBackup ? TwrpExportState.Completed : TwrpExportState.None;
            session.ExportError = null;
            await store.SaveAsync(session, token).ConfigureAwait(false);
        }
        await DataStateAsync(session, TwrpSessionState.ReturningAndroid, progress, token).ConfigureAwait(false);
        await AdbAsync(session.Device.Serial, "reboot", token).ConfigureAwait(false);
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (await IsDataAndroidAsync(session, token).ConfigureAwait(false))
            {
                await CompleteDataAsync(session, progress, token).ConfigureAwait(false);
                return;
            }
            await Task.Delay(PollInterval, token).ConfigureAwait(false);
        }
        throw new TwrpWorkflowException("Twrp_DataAndroidTimeout");
    }

    private async Task<bool> IsDataAndroidAsync(TwrpSession session, CancellationToken token)
    {
        try
        {
            string text = (await ShellAsync(session.Device.Serial, "getprop ro.twrp.boot; getprop sys.boot_completed", token).ConfigureAwait(false)).StandardOutput;
            string[] lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n');
            return lines.Length >= 2 && lines[0].Trim() != "1" && lines[1].Trim() == "1";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }

    private async Task CompleteDataAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        if (session.CliRestore)
        {
            string payload = CliPayloadDirectory(session);
            if (Directory.Exists(payload)) DeleteCheckedDirectory(payload);
        }
        session.AndroidReturned = true;
        session.WorkflowError = null;
        await DataStateAsync(session, TwrpSessionState.Completed, progress, token).ConfigureAwait(false);
        if (session.CliBackup) DeleteCheckedDirectory(session.CacheDirectory!);
    }

    private async Task TrySignalDataStopAsync(TwrpSession session)
    {
        try
        {
            await ShellAsync(session.Device.Serial, $"test \"$(cat /proc/sys/kernel/random/boot_id)\" = {TwrpSafety.Quote(session.RecoveryBootId)} && printf '%s' {TwrpSafety.Quote(session.Id.ToString("N"))} > {TwrpSafety.Quote(TwrpScripts.Work(session) + "/stop-request")}", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) { logger.LogDebug(e, "Data Stop queued for {Operation}.", session.Id); }
    }

    private async Task CheckDataRunnerAliveAsync(TwrpSession session, CancellationToken token)
    {
        string work = TwrpScripts.Work(session);
        var result = await ShellAsync(session.Device.Serial, $$"""
            pid=$(cat {{TwrpSafety.Quote(work + "/pid")}} 2>/dev/null)
            case "$pid" in ''|*[!0-9]*) exit 0 ;; esac
            if [ -r "/proc/$pid/stat" ] && [ "$(awk '{print $22}' /proc/$pid/stat)" = "$(cat {{TwrpSafety.Quote(work + "/pid-start")}})" ]; then printf 1; fi
            """, token).ConfigureAwait(false);
        if (result.StandardOutput.Trim() != "1") throw new TwrpWorkflowException("Twrp_DataDetached");
    }

    private async Task<TwrpSession> RecoverRunnerDataAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        using var operation = Start(session.Device.Serial, token, canCancel: true);
        using var stopCleanup = new StopRegistration(_confirmedStops, operation.OperationId);
        if (session.State == TwrpSessionState.Completed)
        {
            if (session.CliRestore && Directory.Exists(CliPayloadDirectory(session))) DeleteCheckedDirectory(CliPayloadDirectory(session));
            return session;
        }
        try
        {
            if (session.CleanupCompleted && await IsDataAndroidAsync(session, token).ConfigureAwait(false))
            {
                await CompleteDataAsync(session, progress, token).ConfigureAwait(false);
                return session;
            }
            if (!session.RunnerStarted)
            {
                session.State = TwrpSessionState.Failed;
                session.WorkflowError = "Twrp_DataDetached";
            }
            else
            {
                await InspectDataRecoveryAsync(session, token).ConfigureAwait(false);
                if (!session.NativeCompleted)
                {
                    await WaitRunnerDataResultAsync(session, operation.OperationId, progress, token).ConfigureAwait(false);
                }
                if (session.NativeCompleted)
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, operation.CancellationToken);
                    session.StopRequested = false;
                    await FinishDataAsync(session, progress, linked.Token).ConfigureAwait(false);
                    return session;
                }
            }
        }
        catch (Exception exception)
        {
            session.State = session.NativeCompleted ? TwrpSessionState.NativeCompleted : TwrpSessionState.NeedsRecovery;
            session.WorkflowError = exception is TwrpWorkflowException known ? known.MessageKey : "Twrp_DataDetached";
            logger.LogWarning(exception, "Data session {Operation} retained without replay.", session.Id);
        }
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        return session;
    }
}
