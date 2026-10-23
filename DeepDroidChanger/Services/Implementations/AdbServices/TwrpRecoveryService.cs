using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService(
    IAdbCommandService adb, IAdbRootAccessService root, IDeviceActionCoordinatorService coordinator,
    TwrpSessionStore store, TwrpImageService images, ILogger<TwrpRecoveryService> logger) : ITwrpRecoveryService
{
    public string DefaultRescueDirectory => store.CacheRoot;
    public string? GetDefaultImagePath(string deviceMarker) => images.GetDefaultImagePath(deviceMarker);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    internal TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromHours(3);

    public async Task<TwrpSession> RunAsync(TwrpRequest request, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        if (request.SourceArchivePath is not null && request.SourceBackupId is null)
        {
            var archive = await _archives.ReadAsync(request.SourceArchivePath, token).ConfigureAwait(false);
            if (archive.Version is 3 or 4 or 5) return await RunCliRestoreAsync(request, archive, progress, token).ConfigureAwait(false);
        }
        bool cliBackup = request.SourceArchivePath is null && request.SourceBackupId is null;
        if (cliBackup) request = request with { Partitions = new[] { "data" }, StorageRoot = "/data/media/0",
            BackupDirectory = request.BackupDirectory ?? _archives.DefaultDirectory,
            BackupName = request.BackupName ?? _archives.DefaultName("sargo", request.Serial) };
        TwrpSafety.ValidateSerial(request.Serial);
        // The selector lists all supplied assets; only sargo's guarded execution
        // is implemented in this release. Never substitute its image for another.
        if (request.RecoveryDevice is not null && request.RecoveryDevice != "sargo")
            throw new TwrpWorkflowException("Twrp_RecoveryNotImplemented");
        TwrpSafety.ValidateRequestedSelection(request.Partitions);
        if (request.SourceArchivePath is not null && request.SourceBackupId is not null) throw new TwrpWorkflowException("Twrp_InvalidArchive");
        if (request.Confirmation != TwrpSafety.Confirmation) throw new TwrpWorkflowException("Twrp_ConfirmationRequired");
        TwrpSafety.Storage(request.StorageRoot);
        using var operation = Start(request.Serial, token, canCancel: true);
        using var stopCleanup = new StopRegistration(_confirmedStops, operation.OperationId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, operation.CancellationToken);
        token = lifetime.Token;
        string? archivePath = request.SourceArchivePath is null && request.BackupDirectory is not null
            ? _archives.OutputPath(request.BackupDirectory, request.BackupName ?? "") : null;
        if (archivePath is not null) _archives.PrepareOutput(archivePath);
        string? cliName = cliBackup ? TwrpSafety.CliBackupName(archivePath!) : null;
        TwrpBackupManifest? manifest = request.SourceArchivePath is null ? null : await _archives.ReadAsync(request.SourceArchivePath, token).ConfigureAwait(false);
        var preflight = await InspectCoreAsync(request.Serial, token, request.SourceImagePath).ConfigureAwait(false);
        if (!preflight.CanStart) throw new TwrpWorkflowException(preflight.Issues.First());
        var device = preflight.Device!;
        var history = await store.ListAsync(request.Serial, token).ConfigureAwait(false);
        if (history.Any(item => !item.IsTerminal)) throw new TwrpWorkflowException("Twrp_PendingSession");
        string[] requested = request.Partitions.ToArray();
        bool restoring = manifest is not null || request.SourceBackupId is not null;
        string[] selected = requested.Where(id => device.Partitions.Any(p => p.Id == id) && (!restoring || id != "boot")
            && (manifest is null || manifest.Partitions.Contains(id))).ToArray();
        TwrpSession? source = null;
        if (manifest is not null)
        {
            _archives.ValidateSelection(manifest, selected, request.Options?.VerifyDigest == true);
            if (!TwrpSafety.Compatible(manifest.Device, device)) throw new TwrpWorkflowException("Twrp_IncompatibleBackup");
            source = new TwrpSession { Id = manifest.BackupId, Kind = TwrpOperationKind.Backup, State = TwrpSessionState.Completed,
                BootRestored = true, Device = manifest.Device, Partitions = manifest.Partitions,
                Artifacts = manifest.Payload.Select(p => new TwrpArtifact(p.Path[8..], p.Bytes)).ToArray() };
        }
        if (request.SourceBackupId is Guid id)
        {
            source = await store.LoadAsync(id, token).ConfigureAwait(false);
            if (source.Kind != TwrpOperationKind.Backup || source.State != TwrpSessionState.Completed || !source.BootRestored
                || source.Artifacts.Length == 0 || !TwrpSafety.Compatible(source.Device, device))
                throw new TwrpWorkflowException("Twrp_IncompatibleBackup");
            selected = selected.Where(source.Partitions.Contains).ToArray();
            if (request.StorageRoot != source.StorageRoot) throw new TwrpWorkflowException("Twrp_IncompatibleBackup");
            if (source.NativeFolder != TwrpSafety.Storage(source.StorageRoot) + "/DeepDroidChanger/TWRP/" + source.Id.ToString("N"))
                throw new TwrpWorkflowException("Twrp_UnsafePath");
            ValidatePayload(device, selected, ParseArtifacts(string.Join("\n", source.Artifacts.Select(item => $"{item.Name}|{item.Bytes}|{item.Sha256}"))));
        }
        if (selected.Length == 0) throw new TwrpWorkflowException("Twrp_NoSelectedPartitions");
        TwrpSafety.ValidateSelection(device, selected, allowOriginalBoot: !restoring);
        string staging = request.StagingRoot ?? device.StagingCandidates.First();
        if (!device.StagingCandidates.Contains(staging)) throw new TwrpWorkflowException("Twrp_NoStaging");
        var session = new TwrpSession
        {
            Kind = source is null ? TwrpOperationKind.Backup : TwrpOperationKind.Restore,
            Device = device, StagingRoot = staging, StorageRoot = request.StorageRoot,
            RescueDirectory = Path.GetFullPath(request.RescueDirectory), Partitions = selected, SourceBackupId = source?.Id,
            RequestedPartitions = requested, SkippedPartitions = requested.Except(selected).ToArray(), RecoveryDevice = request.RecoveryDevice ?? "sargo",
            SourceImagePath = request.SourceImagePath,
            Options = (request.Options ?? new()) with { Compression = true, GenerateDigest = false }, ArchivePath = archivePath,
            NativeBoot = !restoring && selected.Contains("boot")
        };
        session.CliBackup = cliBackup;
        if (cliBackup) session.Options = new(false, true);
        session.NativeFolder = manifest is not null ? request.StorageRoot + "/DeepDroidChanger/TWRP/" + session.Id.ToString("N")
            : source?.NativeFolder ?? request.StorageRoot + "/DeepDroidChanger/TWRP/" + session.Id.ToString("N");
        if (cliBackup) session.NativeFolder = "/data/media/0/TWRP/BACKUPS/" + request.Serial + "/" + cliName;
        TwrpSafety.ValidateLocalDirectory(store.Root);
        TwrpSafety.ValidateLocalDirectory(session.RescueDirectory);
        if (cliBackup) PrepareCliCache(session);
        await store.SaveAsync(session, token).ConfigureAwait(false);
        bool critical = false;
        try
        {
            progress?.Report(new(session.Id, session.State, "Twrp_State_Preparing"));
            ReportSkipped(session, progress);
            if (manifest is not null)
                await ImportPayloadAsync(session, request.SourceArchivePath!, manifest, progress, token).ConfigureAwait(false);
            await SaveOriginalAsync(session, token).ConfigureAwait(false);
            await StateAsync(session, TwrpSessionState.OriginalSaved, progress, token).ConfigureAwait(false);
            await SaveRescueAsync(session, token).ConfigureAwait(false);
            if (session.Kind == TwrpOperationKind.Backup && !session.NativeBoot && session.Partitions.SequenceEqual(new[] { "boot" }))
            {
                token.ThrowIfCancellationRequested();
                session.Artifacts = [OriginalBootArtifact(session)];
                session.BootRestored = true; // Boot was saved, never overwritten.
                await StateAsync(session, TwrpSessionState.BootRestored, progress, token).ConfigureAwait(false);
                await StateAsync(session, TwrpSessionState.Completed, progress, token).ConfigureAwait(false);
                if (session.ArchivePath is not null) await ExportAsync(session, progress, token).ConfigureAwait(false);
                return session;
            }
            string image = store.FileFor(session.Id, "recovery.img");
            session.RuntimeImageHash = await images.BuildAsync(session, image, token).ConfigureAwait(false);
            await root.ExecuteAsRootAsync(request.Serial, async ct =>
            {
                await TransferAsync(request.Serial, image, TwrpScripts.Remote(session) + "/recovery.img", push: true, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            await StateAsync(session, TwrpSessionState.ImagePrepared, progress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            critical = true;
            await StateAsync(session, TwrpSessionState.FlashIntent, progress, CancellationToken.None).ConfigureAwait(false);
            await FlashAsync(session).ConfigureAwait(false);
            await StateAsync(session, TwrpSessionState.TwrpFlashed, progress, CancellationToken.None).ConfigureAwait(false);
            if (_confirmedStops.ContainsKey(operation.OperationId))
            {
                // Persist a queued Stop before the reboot, so the guard does
                // not start native restore if the PC disappears during transition.
                await root.ExecuteAsRootAsync(request.Serial, async ct =>
                    await ShellAsync(request.Serial, $"printf '%s' {TwrpSafety.Quote(session.Id.ToString("N"))} > {TwrpSafety.Quote(TwrpScripts.Remote(session) + "/stop-request")} && sync", ct).ConfigureAwait(false), CancellationToken.None).ConfigureAwait(false);
            }
            await AdbAsync(request.Serial, "reboot recovery", CancellationToken.None).ConfigureAwait(false);
            await StateAsync(session, TwrpSessionState.Rebooted, progress, CancellationToken.None).ConfigureAwait(false);
            await CollectResultAsync(session, progress, () => _confirmedStops.ContainsKey(operation.OperationId), cliBackup ? token : CancellationToken.None).ConfigureAwait(false);
            critical = false;
            if (session.Kind == TwrpOperationKind.Backup && session.State == TwrpSessionState.Completed && session.ArchivePath is not null)
            {
                if (cliBackup) await ExportCliAsync(session, progress, token).ConfigureAwait(false);
                else await ExportAsync(session, progress, token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "TWRP operation {Operation} failed at {State}.", session.Id, session.State);
            if (!critical && session.State == TwrpSessionState.Completed && session.ArchivePath is not null)
            {
                // Export/journal errors must not erase a successful native backup.
                session.ExportState = token.IsCancellationRequested ? TwrpExportState.Canceled : TwrpExportState.Failed;
                session.ExportError = token.IsCancellationRequested ? "Twrp_ExportCanceled" : "Twrp_ExportFailed";
                await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
                return session;
            }
            session.StopRequested = _confirmedStops.ContainsKey(operation.OperationId);
            session.WorkflowError ??= session.StopRequested ? "Twrp_Stopped" : exception is TwrpWorkflowException known ? known.MessageKey : "Twrp_WorkflowFailed";
            if (critical)
            {
                // Do not cancel an independently running native operation on loss of the PC.
                session.State = TwrpSessionState.NeedsRecovery;
                await TryRecoverBootAsync(session).ConfigureAwait(false);
            }
            else session.State = TwrpSessionState.Failed;
            await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        return session;
    }

}
