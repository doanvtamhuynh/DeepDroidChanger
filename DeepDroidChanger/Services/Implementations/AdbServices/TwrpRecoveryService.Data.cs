using System.IO;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private static bool IsDataDevice(string marker) => marker is "sargo" or "starlte" or "tissot";
    private static TwrpDeviceInfo DataDevice(string serial, string marker) =>
        new(serial, marker, "", "", 0, "", "", 0, "", "", 0, [new("data", "/data", "", 0)], []);

    private async Task<TwrpOperation> RunDataAsync(TwrpRequest request, IProgress<TwrpProgress>? progress, CancellationToken externalToken)
    {
        // Validate argument syntax, never query or compare the UI's serial again.
        string serial = request.Serial;
        TwrpSafety.ValidateSerial(serial);
        if (request.Confirmation != TwrpSafety.Confirmation) throw new TwrpWorkflowException("Twrp_ConfirmationRequired");
        using var ownership = Start(serial, externalToken);
        using var stopCleanup = new StopRegistration(_confirmedStops, ownership.OperationId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(externalToken, ownership.CancellationToken);
        CancellationToken token = lifetime.Token;
        if (_unsafeDevices.ContainsKey(serial)) throw new TwrpWorkflowException("Twrp_DataNativeActive");
        bool restore = request.SourceArchivePath is not null;
        string marker = request.RecoveryDevice ?? "recovery"; // configuration snapshot, not a live identity check
        string archivePath = restore ? Path.GetFullPath(request.SourceArchivePath!)
            : _archives.OutputPath(request.BackupDirectory ?? _archives.DefaultDirectory,
                request.BackupName ?? _archives.DefaultName(marker, serial));
        if (!restore) _archives.PrepareOutput(archivePath);
        string name = TwrpSafety.CliBackupName(archivePath);
        var operation = new TwrpOperation
        {
            Device = DataDevice(serial, marker), Kind = restore ? TwrpOperationKind.Restore : TwrpOperationKind.Backup,
            RecoveryDevice = marker, ArchivePath = archivePath,
            Options = new(false, true)
        };
        try
        {
            if (restore) cache.Create(operation);
            SetDataState(operation, TwrpOperationState.Preparing, progress);
            TwrpBackupManifest? manifest = restore
                ? await PrepareDataRestoreAsync(operation, progress, token).ConfigureAwait(false)
                : null;
            operation.NativeFolder = "/data/media/0/TWRP/BACKUPS/" + serial + "/"
                + (restore ? "restore_" + operation.Id.ToString("N") : name);
            SetDataState(operation, TwrpOperationState.EnteringRecovery, progress);
            await EnterRecoveryCoreAsync(serial, request.RecoveryDevice, request.SourceImagePath,
                new DataEntryProgress(key => ReportData(operation, progress, key)), token).ConfigureAwait(false);
            if (!restore) cache.Create(operation);
            SetDataState(operation, TwrpOperationState.RecoveryReady, progress);
            await AcquireNativeOwnershipAsync(operation, token).ConfigureAwait(false);
            await ReserveDirectDataPayloadAsync(operation, token).ConfigureAwait(false);
            await PrepareDirectScreenAsync(operation, progress, token).ConfigureAwait(false);
            if (restore) await UploadDirectDataAsync(operation, manifest!, progress, token).ConfigureAwait(false);
            await RunDirectDataCommandAsync(operation, name, progress, token).ConfigureAwait(false);
            if (!restore) await CreateDataBackupArchiveAsync(operation, progress, token).ConfigureAwait(false);
            SetDataState(operation, TwrpOperationState.Cleaning, progress);
            await CleanupDataRecoveryAsync(operation, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await AdbAsync(serial, "reboot", token).ConfigureAwait(false);
            LogOperation(operation, "Removing PC temporary files.");
            cache.Clean(operation, success: true);
            operation.State = TwrpOperationState.Completed;
            logger.LogInformation("TWRP operation {Operation} completed; PC cleanup completed.", operation.Id);
            try { progress?.Report(new(operation.Id, operation.State, "Twrp_State_Completed")); }
            catch (Exception notificationError)
            {
                logger.LogWarning(notificationError, "Cannot display completion for {Operation}.", operation.Id);
            }
        }
        catch (Exception exception)
        {
            await HandleDataFailureAsync(operation, exception, ownership.OperationId, progress, token).ConfigureAwait(false);
        }
        return operation;
    }

    private async Task<TwrpBackupManifest> PrepareDataRestoreAsync(TwrpOperation operation,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        ReportData(operation, progress, "Twrp_Extracting");
        // The dialog checks device compatibility; extraction validates archive contents.
        var manifest = await _archives.ExtractDataRestoreAsync(operation.ArchivePath!, operation.CacheDirectory!, token).ConfigureAwait(false);
        operation.Artifacts = TwrpArchiveService.NativePayloads(manifest)
            .Select(p => new TwrpArtifact(p.Path[(manifest.Name.Length + 1)..], p.Bytes)).ToArray();
        return manifest;
    }

    private async Task CreateDataBackupArchiveAsync(TwrpOperation operation,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        await DownloadDirectDataAsync(operation, progress, token).ConfigureAwait(false);
        SetDataState(operation, TwrpOperationState.Packaging, progress);
        await _archives.WriteCliAsync(operation, token,
            () => SetDataState(operation, TwrpOperationState.Verifying, progress)).ConfigureAwait(false);
        LogOperation(operation, "ZIP verified and published: " + operation.ArchivePath);
    }

    private async Task CleanupDataRecoveryAsync(TwrpOperation operation, CancellationToken token)
    {
        await RestoreDirectScreenAsync(operation, token).ConfigureAwait(false);
        await CleanupDataPayloadAsync(operation, token).ConfigureAwait(false);
        await ReleaseNativeOwnershipAsync(operation, token).ConfigureAwait(false);
    }

    private async Task HandleDataFailureAsync(TwrpOperation operation, Exception exception, Guid ownershipId,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        // Entry errors still receive a fresh log-only attempt; no workflow is loaded.
        if (operation.CacheDirectory is null) cache.Create(operation);
        operation.StopRequested = token.IsCancellationRequested || _confirmedStops.ContainsKey(ownershipId);
        operation.NativeStateUnknown = operation.NativeStarted && !operation.NativeCompleted;
        if (operation.NativeStateUnknown) _unsafeDevices.TryAdd(operation.Device.Serial, 0);
        operation.WorkflowError = exception switch
        {
            TwrpWorkflowException { MessageKey: "Twrp_NativeTimeout" } => "Twrp_NativeTimeout",
            _ when operation.NativeStateUnknown => "Twrp_DirectResultUnknown",
            _ when operation.StopRequested => "Twrp_Stopped",
            TwrpWorkflowException known => known.MessageKey,
            _ => "Twrp_WorkflowFailed"
        };
        if (operation.StopRequested) SetDataState(operation, TwrpOperationState.Stopping, progress);
        LogOperation(operation, $"{(operation.StopRequested ? "Stop" : "Failure")}: {exception}. Native state unknown: {operation.NativeStateUnknown}");
        await CaptureDataLogsAsync(operation).ConfigureAwait(false);
        // Killing adb does not imply TWRP stopped. Preserve its interlock and remote data if uncertain.
        if (!operation.NativeStateUnknown)
        {
            using var cleanupLimit = new CancellationTokenSource(CommandTimeout);
            try
            {
                await CleanupDataRecoveryAsync(operation, cleanupLimit.Token).ConfigureAwait(false);
                LogOperation(operation, "Remote cleanup completed.");
            }
            catch (Exception cleanupError)
            {
                LogOperation(operation, "Remote cleanup failed: " + cleanupError.Message);
                logger.LogWarning(cleanupError, "Remote cleanup failed for {Operation}.", operation.Id);
            }
        }
        try
        {
            string partial = operation.ArchivePath + "." + operation.Id.ToString("N") + ".partial";
            if (operation.CliBackup && File.Exists(partial)) TwrpOperationCache.DeleteChecked(partial);
            cache.Clean(operation, success: false);
            LogOperation(operation, "PC cleanup completed; only logs retained.");
        }
        catch (Exception cleanupError)
        {
            LogOperation(operation, "PC cleanup FAILED; temporary files may remain: " + cleanupError.Message);
            operation.WorkflowError = "Twrp_CleanupFailed";
        }
        SetDataState(operation, operation.StopRequested ? TwrpOperationState.Stopped : TwrpOperationState.Failed, progress);
        ReportData(operation, progress, operation.WorkflowError);
        logger.LogWarning(exception, "TWRP operation {Operation} failed. Native state unknown: {Unknown}.", operation.Id, operation.NativeStateUnknown);
    }

    private async Task CaptureDataLogsAsync(TwrpOperation operation)
    {
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string text = (await ShellAsync(operation.Device.Serial, "cat /tmp/recovery.log 2>/dev/null || true", limit.Token).ConfigureAwait(false)).StandardOutput;
            Log(operation, "recovery.log", text);
        }
        catch (Exception exception) { LogOperation(operation, "Recovery log unavailable: " + exception.Message); }
    }

    private void SetDataState(TwrpOperation operation, TwrpOperationState state, IProgress<TwrpProgress>? progress)
    {
        operation.State = state;
        ReportData(operation, progress, "Twrp_State_" + state);
    }

    private void ReportData(TwrpOperation operation, IProgress<TwrpProgress>? progress, string key)
    {
        LogOperation(operation, key);
        progress?.Report(new(operation.Id, operation.State, key));
    }

    private sealed class DataEntryProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string key) => report(key);
    }
}
