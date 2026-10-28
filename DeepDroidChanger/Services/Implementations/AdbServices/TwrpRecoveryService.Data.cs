using System.IO;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private static bool IsDataDevice(string marker) => marker is "sargo" or "starlte" or "tissot";
    private static TwrpDeviceInfo DataDevice(string serial, string marker) =>
        new(serial, marker, "", "", 0, "", "", 0, "", "", 0, [new("data", "/data", "", 0)], []);

    private Task<TwrpSession> RecoverDataAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token) =>
        session.DirectCommands
            ? RecoverDirectDataAsync(session, progress, token)
            : RecoverRunnerDataAsync(session, progress, token);

    private async Task<TwrpSession> RunDataAsync(TwrpRequest request, IProgress<TwrpProgress>? progress, CancellationToken externalToken)
    {
        TwrpSafety.ValidateSerial(request.Serial);
        if (request.Confirmation != TwrpSafety.Confirmation) throw new TwrpWorkflowException("Twrp_ConfirmationRequired");
        using var operation = Start(request.Serial, externalToken, canCancel: true);
        using var stopCleanup = new StopRegistration(_confirmedStops, operation.OperationId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(externalToken, operation.CancellationToken);
        CancellationToken token = lifetime.Token;
        if ((await store.ListAsync(request.Serial, token).ConfigureAwait(false)).Any(s => !s.IsTerminal))
            throw new TwrpWorkflowException("Twrp_PendingSession");
        string marker = await GetDeviceMarkerAsync(request.Serial, token).ConfigureAwait(false);
        if (!IsDataDevice(marker)) throw new TwrpWorkflowException("Twrp_DataIdentityRequired");
        if (request.RecoveryDevice is not null && request.RecoveryDevice != marker)
            throw new TwrpWorkflowException("Twrp_BackupTypeMismatch");
        bool restore = request.SourceArchivePath is not null;
        string archivePath = restore ? Path.GetFullPath(request.SourceArchivePath!)
            : _archives.OutputPath(request.BackupDirectory ?? _archives.DefaultDirectory,
                request.BackupName ?? _archives.DefaultName(marker, request.Serial));
        if (!restore) _archives.PrepareOutput(archivePath);
        TwrpSafety.CliBackupName(archivePath);
        var session = new TwrpSession
        {
            ExecutionMode = TwrpExecutionMode.ExternalRecovery, DirectCommands = true, Device = DataDevice(request.Serial, marker),
            Kind = restore ? TwrpOperationKind.Restore : TwrpOperationKind.Backup,
            RecoveryDevice = marker, SourceImagePath = request.SourceImagePath,
            SourceImageHash = "", GuardVersion = "data-direct-1", Partitions = ["data"], RequestedPartitions = ["data"],
            StorageRoot = "/data/media/0", ArchivePath = archivePath, CliBackup = !restore, CliRestore = restore,
            Options = new(false, true), RescueDirectory = store.CacheRoot
        };
        PrepareCliCache(session);
        await store.SaveAsync(session, token).ConfigureAwait(false);
        try
        {
            TwrpBackupManifest? manifest = null;
            string name = TwrpSafety.CliBackupName(archivePath);
            if (restore)
            {
                manifest = await PrepareDataRestoreAsync(session, progress, token).ConfigureAwait(false);
                name = manifest.Name;
            }
            session.NativeFolder = session.StorageRoot + "/TWRP/BACKUPS/" + request.Serial + "/"
                + (restore ? "restore_" + session.Id.ToString("N") : name);
            TwrpSafety.RemoteRoot(session.NativeFolder);
            await DataStateAsync(session, TwrpSessionState.EnteringRecovery, progress, token).ConfigureAwait(false);
            await EnterRecoveryCoreAsync(request.Serial, marker, request.SourceImagePath,
                new DataEntryProgress(key => ReportData(session, progress, key)), token, rememberIdentity: false).ConfigureAwait(false);
            await PrepareDirectScreenAsync(session, progress, token).ConfigureAwait(false);
            if (restore) await UploadDirectDataAsync(session, manifest!, progress, token).ConfigureAwait(false);
            session.NativeStarted = true; // Persist intent; reconnect must never replay a direct command.
            await DataStateAsync(session, TwrpSessionState.RecoveryRunning, progress, token).ConfigureAwait(false);
            await RunDirectDataCommandAsync(session, name, progress, token).ConfigureAwait(false);
            await FinishDirectDataAsync(session, progress, token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            session.StopRequested = _confirmedStops.ContainsKey(operation.OperationId);
            if (session.NativeStarted) await CaptureDataLogsAsync(session, CancellationToken.None).ConfigureAwait(false);
            session.WorkflowError = session.StopRequested ? "Twrp_Stopped"
                : exception is TwrpWorkflowException known ? known.MessageKey
                : exception is OperationCanceledException ? "Twrp_DirectResultUnknown" : "Twrp_WorkflowFailed";
            if (session.NativeCompleted && session.CliBackup)
            {
                session.ExportState = exception is OperationCanceledException ? TwrpExportState.Canceled : TwrpExportState.Failed;
                session.ExportError = session.WorkflowError;
            }
            if (session.State != TwrpSessionState.Failed)
                session.State = session.NativeStarted && !session.NativeCompleted ? TwrpSessionState.NeedsRecovery
                    : session.NativeCompleted ? TwrpSessionState.NativeCompleted : TwrpSessionState.Failed;
            await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            logger.LogWarning(exception, "Data workflow {Operation} retained at {State}.", session.Id, session.State);
        }
        return session;
    }

    private async Task<TwrpBackupManifest> PrepareDataRestoreAsync(TwrpSession session,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        ReportData(session, progress, "Twrp_Extracting");
        var manifest = await _archives.ExtractDataRestoreAsync(session.ArchivePath!, session.CacheDirectory!, m =>
        {
            if (m.Device.Serial != session.Device.Serial || m.Device.Codename != session.Device.Codename)
                throw new TwrpWorkflowException("Twrp_BackupTypeMismatch");
            return Task.CompletedTask;
        }, token).ConfigureAwait(false);
        session.SourceBackupId = manifest.BackupId;
        session.Artifacts = TwrpArchiveService.NativePayloads(manifest)
            .Select(p => new TwrpArtifact(p.Path[(manifest.Name.Length + 1)..], p.Bytes)).ToArray();
        session.Options = new(false, true, VerifyDigest: _archives.HasDigests(manifest, ["data"]));
        return manifest;
    }

    private async Task PackageDataBackupAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        session.ExportState = TwrpExportState.Packaging;
        ReportData(session, progress, "Twrp_Packaging");
        if (File.Exists(session.ArchivePath))
            await _archives.VerifyCliAsync(session, session.ArchivePath!, token).ConfigureAwait(false);
        else
            await _archives.WriteCliAsync(session, token).ConfigureAwait(false);
    }

    private async Task CleanupDataPayloadAsync(TwrpSession session, CancellationToken token)
    {
        string path = session.NativeFolder;
        TwrpSafety.RemoteRoot(path);
        string prefix = session.StorageRoot + "/TWRP/BACKUPS/" + session.Device.Serial + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !TwrpSafety.SafeName().IsMatch(path[prefix.Length..])) throw new TwrpWorkflowException("Twrp_UnsafePath");
        string recoveryIdentity = session.DirectCommands ? "" : "test \"$(cat /proc/sys/kernel/random/boot_id)\" = " + TwrpSafety.Quote(session.RecoveryBootId);
        await ShellAsync(session.Device.Serial, $$"""
            set -eu
            {{recoveryIdentity}}
            path={{TwrpSafety.Quote(path)}}
            if [ -e "$path" ] || [ -L "$path" ]; then
                test "$(readlink -f "$path")" = "$path"
                test ! -L "$path/.deepdroid-operation"
                test "$(cat "$path/.deepdroid-operation")" = {{TwrpSafety.Quote(session.Id.ToString("N"))}}
                rm -rf -- "$path"
            fi
            """, token).ConfigureAwait(false);
    }

    private async Task CaptureDataLogsAsync(TwrpSession session, CancellationToken token)
    {
        try
        {
            string text = (await ShellAsync(session.Device.Serial,
                $"cat {TwrpSafety.Quote(TwrpScripts.Work(session) + "/runner.log")} {TwrpSafety.Quote(TwrpScripts.Work(session) + "/native.log")} /tmp/recovery.log 2>/dev/null || true", token).ConfigureAwait(false)).StandardOutput;
            session.RecoveryLog += "\n" + text;
            await File.WriteAllTextAsync(Path.Combine(session.CacheDirectory!, "recovery.log"), session.RecoveryLog, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) { logger.LogDebug(e, "Unable to collect Data logs for {Operation}.", session.Id); }
    }

    private async Task DataStateAsync(TwrpSession session, TwrpSessionState state, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        session.State = state;
        await store.SaveAsync(session, token).ConfigureAwait(false);
        ReportData(session, progress, "Twrp_State_" + state);
    }

    private static void ReportData(TwrpSession session, IProgress<TwrpProgress>? progress, string key) =>
        progress?.Report(new(session.Id, session.State, key, Detail: session.CacheDirectory));

    private sealed class DataEntryProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string key) => report(key);
    }
}
