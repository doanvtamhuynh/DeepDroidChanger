using System.IO;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private async Task<TwrpSession> RunCliRestoreAsync(TwrpRequest request, TwrpBackupManifest expected,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        TwrpSafety.ValidateSerial(request.Serial);
        if (request.Confirmation != TwrpSafety.Confirmation) throw new TwrpWorkflowException("Twrp_ConfirmationRequired");
        if (request.RecoveryDevice is not null && request.RecoveryDevice != "sargo") throw new TwrpWorkflowException("Twrp_RecoveryNotImplemented");
        using var operation = Start(request.Serial, token, canCancel: true);
        using var stopCleanup = new StopRegistration(_confirmedStops, operation.OperationId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, operation.CancellationToken);
        token = lifetime.Token;
        var history = await store.ListAsync(request.Serial, token).ConfigureAwait(false);
        if (history.Any(s => !s.IsTerminal)) throw new TwrpWorkflowException("Twrp_PendingSession");
        var session = new TwrpSession
        {
            Kind = TwrpOperationKind.Restore, CliRestore = true, ArchivePath = Path.GetFullPath(request.SourceArchivePath!),
            StorageRoot = "/data/media/0", RescueDirectory = Path.GetFullPath(request.RescueDirectory),
            SourceImagePath = request.SourceImagePath, RecoveryDevice = request.RecoveryDevice ?? "sargo",
            SourceBackupId = expected.BackupId, Partitions = ["data"], RequestedPartitions = ["data"]
        };
        bool critical = false, cacheOwned = false, validated = false, journalCreated = false;
        try
        {
            PrepareCliCache(session);
            cacheOwned = true;
            progress?.Report(new(session.Id, session.State, "Twrp_Extracting", session.CacheDirectory));
            var manifest = await _archives.ExtractCliRestoreAsync(session.ArchivePath, session.CacheDirectory!, token, payloadSubdirectory: true).ConfigureAwait(false);
            if (manifest.Version != expected.Version || manifest.BackupId != expected.BackupId || manifest.Name != expected.Name || !manifest.Payload.SequenceEqual(expected.Payload)
                || !TwrpSafety.Compatible(manifest.Device, expected.Device)) throw new TwrpWorkflowException("Twrp_InvalidArchive");
            string marker = await GetDeviceMarkerAsync(request.Serial, token).ConfigureAwait(false);
            // Archive validation has already checked the recovery image against this marker,
            // including type.json consistency for older version 3 ZIPs.
            if (manifest.Device.Codename != marker || manifest.Device.Serial != request.Serial
                || manifest.Device.Codename != session.RecoveryDevice) throw new TwrpWorkflowException("Twrp_BackupTypeMismatch");
            var preflight = await InspectCoreAsync(request.Serial, token, request.SourceImagePath).ConfigureAwait(false);
            if (!preflight.CanStart) throw new TwrpWorkflowException(preflight.Issues.First());
            session.Device = preflight.Device!;
            if (!TwrpSafety.CompatibleBackup(manifest, session.Device)) throw new TwrpWorkflowException("Twrp_BackupTypeMismatch");
            string staging = request.StagingRoot ?? session.Device.StagingCandidates.First();
            if (!session.Device.StagingCandidates.Contains(staging)) throw new TwrpWorkflowException("Twrp_NoStaging");
            session.StagingRoot = staging;
            session.NativeFolder = "/data/media/0/TWRP/BACKUPS/" + request.Serial + "/" + manifest.Name;
            session.Artifacts = TwrpArchiveService.NativePayloads(manifest).Select(p => new TwrpArtifact(p.Path[(manifest.Name.Length + 1)..], p.Bytes)).ToArray();
            session.Options = manifest.Options with { VerifyDigest = true };
            validated = true;
            TwrpSafety.ValidateLocalDirectory(store.Root);
            TwrpSafety.ValidateLocalDirectory(session.RescueDirectory);
            await store.SaveAsync(session, token).ConfigureAwait(false);
            journalCreated = true;
            await UploadCliRestoreAsync(session, manifest.Name, progress, token).ConfigureAwait(false);
            await SaveOriginalAsync(session, token).ConfigureAwait(false);
            // Historical boot from old ZIPs stays under payload/, never at this live rescue path.
            await StateAsync(session, TwrpSessionState.OriginalSaved, progress, token).ConfigureAwait(false);
            await SaveRescueAsync(session, token).ConfigureAwait(false);
            string image = store.FileFor(session.Id, "recovery.img");
            session.RuntimeImageHash = await images.BuildAsync(session, image, token).ConfigureAwait(false);
            await root.ExecuteAsRootAsync(request.Serial, async ct =>
                await TransferAsync(request.Serial, image, TwrpScripts.Remote(session) + "/recovery.img", true, ct).ConfigureAwait(false), token).ConfigureAwait(false);
            await StateAsync(session, TwrpSessionState.ImagePrepared, progress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            critical = true;
            await StateAsync(session, TwrpSessionState.FlashIntent, progress, CancellationToken.None).ConfigureAwait(false);
            await FlashAsync(session).ConfigureAwait(false);
            await StateAsync(session, TwrpSessionState.TwrpFlashed, progress, CancellationToken.None).ConfigureAwait(false);
            if (_confirmedStops.ContainsKey(operation.OperationId))
                await root.ExecuteAsRootAsync(request.Serial, async ct =>
                    await ShellAsync(request.Serial, $"printf '%s' {TwrpSafety.Quote(session.Id.ToString("N"))} > {TwrpSafety.Quote(TwrpScripts.Remote(session) + "/stop-request")}; sync", ct).ConfigureAwait(false), CancellationToken.None).ConfigureAwait(false);
            await AdbAsync(request.Serial, "reboot recovery", CancellationToken.None).ConfigureAwait(false);
            await StateAsync(session, TwrpSessionState.Rebooted, progress, CancellationToken.None).ConfigureAwait(false);
            await CollectResultAsync(session, progress, () => _confirmedStops.ContainsKey(operation.OperationId)).ConfigureAwait(false);
            critical = false;
            if (session.State == TwrpSessionState.Completed && !session.StopRequested)
                await CleanupCliRestoreAsync(session, progress, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TWRP ZIP restore {Operation} failed at {State}.", session.Id, session.State);
            session.StopRequested = _confirmedStops.ContainsKey(operation.OperationId);
            session.WorkflowError ??= session.StopRequested ? "Twrp_Stopped" : ex is TwrpWorkflowException known ? known.MessageKey : "Twrp_WorkflowFailed";
            if (critical)
            {
                session.State = TwrpSessionState.NeedsRecovery;
                await TryRecoverBootAsync(session).ConfigureAwait(false);
            }
            else session.State = TwrpSessionState.Failed;
            if (cacheOwned && !validated) DeleteCliCache(session);
            if (journalCreated) await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            else throw;
        }
        return session;
    }

    private async Task UploadCliRestoreAsync(TwrpSession session, string name, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        CheckCliCache(session);
        progress?.Report(new(session.Id, session.State, "Twrp_Uploading", session.NativeFolder));
        string alias = "/sdcard/TWRP/BACKUPS/" + session.Device.Serial + "/" + name;
        await root.ExecuteAsRootAsync(session.Device.Serial, async ct =>
        {
            await ShellAsync(session.Device.Serial, $$"""
                set -eu
                test "$(getprop ro.deepdroid.device)" = {{TwrpSafety.Quote(session.Device.Codename)}}
                test ! -e {{TwrpSafety.Quote(session.NativeFolder)}}
                test ! -e {{TwrpSafety.Quote(alias)}}
                mkdir -p {{TwrpSafety.Quote(session.NativeFolder[..session.NativeFolder.LastIndexOf('/')])}}
                mkdir {{TwrpSafety.Quote(session.NativeFolder)}}
                test "$(readlink -f {{TwrpSafety.Quote(session.NativeFolder)}})" = {{TwrpSafety.Quote(session.NativeFolder)}}
                printf '%s' {{TwrpSafety.Quote(session.Id.ToString("N"))}} > {{TwrpSafety.Quote(session.NativeFolder + "/.deepdroid-operation")}}
                test "$(cat {{TwrpSafety.Quote(alias + "/.deepdroid-operation")}})" = {{TwrpSafety.Quote(session.Id.ToString("N"))}}
                """, ct).ConfigureAwait(false);
            foreach (var file in session.Artifacts)
            {
                string local = Path.Combine(CliPayloadDirectory(session), name, file.Name);
                if (File.GetAttributes(local).HasFlag(FileAttributes.ReparsePoint) || new FileInfo(local).Length != file.Bytes) throw new TwrpWorkflowException("Twrp_InvalidArchive");
                await PayloadTransferAsync(session.Device.Serial, local, session.NativeFolder + "/" + file.Name, true, ct).ConfigureAwait(false);
            }
        }, token).ConfigureAwait(false);
    }

    private void DeleteCliCache(TwrpSession session)
    {
        CheckCliCache(session);
        DeleteCheckedDirectory(session.CacheDirectory!);
    }
    private void CleanupCliPayload(TwrpSession session)
    {
        CheckCliCache(session);
        string payload = CliPayloadDirectory(session);
        if (Directory.Exists(payload)) DeleteCheckedDirectory(payload);
        if (session.CacheLayoutVersion == 1)
        {
            string image = store.FileFor(session.Id, "recovery.img");
            if (File.Exists(image)) File.Delete(image);
        }
    }
    private async Task CleanupCliRestoreAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (session.State != TwrpSessionState.Completed || !session.BootRestored || session.StopRequested) throw new TwrpWorkflowException("Twrp_PendingSession");
        CheckCliCache(session);
        string name = session.NativeFolder[(session.NativeFolder.LastIndexOf('/') + 1)..];
        if (TwrpSafety.CliBackupName(name + ".zip") != name || session.NativeFolder != "/data/media/0/TWRP/BACKUPS/" + session.Device.Serial + "/" + name) throw new TwrpWorkflowException("Twrp_UnsafePath");
        progress?.Report(new(session.Id, session.State, "Twrp_CleaningRestore", session.CacheDirectory));
        await root.ExecuteAsRootAsync(session.Device.Serial, async ct =>
            await ShellAsync(session.Device.Serial, $$"""
                set -eu
                test "$(getprop ro.deepdroid.device)" = {{TwrpSafety.Quote(session.Device.Codename)}}
                path={{TwrpSafety.Quote(session.NativeFolder)}}
                if [ -e "$path" ]; then
                    test "$(readlink -f "$path")" = "$path"
                    test ! -L "$path/.deepdroid-operation"
                    test "$(cat "$path/.deepdroid-operation")" = {{TwrpSafety.Quote(session.Id.ToString("N"))}}
                    rm -rf -- "$path"
                fi
                """, ct).ConfigureAwait(false), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        CleanupCliPayload(session);
        session.CleanupCompleted = true;
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
    }
}
