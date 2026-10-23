using System.IO;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private void PrepareCliCache(TwrpSession session)
    {
        session.CacheDirectory = CliCachePath(session);
        TwrpArchiveService.CheckDirectory(session.CacheDirectory);
        if (Directory.Exists(session.CacheDirectory) || File.Exists(session.CacheDirectory)) throw new TwrpWorkflowException("Twrp_CacheExists");
        Directory.CreateDirectory(session.CacheDirectory);
        using var marker = new FileStream(Path.Combine(session.CacheDirectory, ".operation"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        marker.Write(System.Text.Encoding.ASCII.GetBytes(session.Id.ToString("N")));
        marker.Flush(flushToDisk: true);
        session.CacheLayoutVersion = 1;
        session.RescueDirectory = session.CacheDirectory;
    }
    private async Task AcceptCliPayloadAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        string work = TwrpScripts.Work(session);
        try
        {
            if (session.NativeFolder != CliDevicePath(session)) throw new TwrpWorkflowException("Twrp_UnsafePath");
            CheckCliCache(session);
            if (!session.PayloadDownloaded)
            {
                await ReadArtifactsAsync(session).ConfigureAwait(false);
                if (!session.Artifacts.Any(f => f.Name == "recovery.log" && f.Bytes > 0)) throw new TwrpWorkflowException("Twrp_InvalidManifest");
                session.NativeBackupCompleted = true;
                await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
                await DownloadCliCacheAsync(session, progress, token).ConfigureAwait(false);
            }
            await ShellAsync(session.Device.Serial, $"printf '%s' {TwrpSafety.Quote(session.Id.ToString("N"))} > {TwrpSafety.Quote(work + "/payload-pulled")}; sync", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            session.ExportError = token.IsCancellationRequested ? "Twrp_ExportCanceled" : ex is TwrpWorkflowException known ? known.MessageKey : "Twrp_TransferFailed";
            session.ExportState = token.IsCancellationRequested ? TwrpExportState.Canceled : TwrpExportState.Failed;
            await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            await ShellAsync(session.Device.Serial, $"printf '%s' {TwrpSafety.Quote(session.Id.ToString("N"))} > {TwrpSafety.Quote(work + "/pull-failed")}; sync", CancellationToken.None).ConfigureAwait(false);
        }
    }
    private async Task DownloadCliCacheAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        CheckCliCache(session);
        string folder = Path.Combine(CliPayloadDirectory(session), TwrpSafety.CliBackupName(session.ArchivePath!));
        TwrpArchiveService.CheckDirectory(folder);
        Directory.CreateDirectory(folder);
        session.ExportState = TwrpExportState.Downloading;
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        progress?.Report(new(session.Id, session.State, "Twrp_Downloading", session.CacheDirectory));
        foreach (var file in session.Artifacts)
        {
            token.ThrowIfCancellationRequested();
            string local = Path.Combine(folder, file.Name);
            if (File.Exists(local) && File.GetAttributes(local).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
            if (File.Exists(local) && new FileInfo(local).Length == file.Bytes) continue;
            string partial = local + ".partial";
            if (File.Exists(partial) && File.GetAttributes(partial).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
            await PayloadTransferAsync(session.Device.Serial, session.NativeFolder + "/" + file.Name, partial, false, token).ConfigureAwait(false);
            if (new FileInfo(partial).Length != file.Bytes) throw new TwrpWorkflowException("Twrp_TransferFailed");
            File.Move(partial, local, overwrite: true);
        }
        session.PayloadDownloaded = true;
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
    }
    private async Task ExportCliAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        try
        {
            if (!session.BootRestored || !session.PayloadDownloaded) throw new TwrpWorkflowException("Twrp_InvalidManifest");
            if (!File.Exists(session.ArchivePath))
            {
                CheckCliCache(session);
                session.ExportState = TwrpExportState.Packaging;
                await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
                progress?.Report(new(session.Id, session.State, "Twrp_Packaging"));
                await _archives.WriteCliAsync(session, token).ConfigureAwait(false);
            }
            await _archives.VerifyCliAsync(session, session.ArchivePath!, token).ConfigureAwait(false);
            if (session.NativeFolder != CliDevicePath(session)) throw new TwrpWorkflowException("Twrp_UnsafePath");
            progress?.Report(new(session.Id, session.State, "Twrp_CleaningBackup"));
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
            DeleteCompletedBackupCache(session);
            session.CleanupCompleted = true;
            session.ExportState = TwrpExportState.Completed;
            session.ExportError = null;
            progress?.Report(new(session.Id, session.State, "Twrp_ExportDone", session.ArchivePath));
            // The journal belonged to the deleted cache. Saving it again would
            // recreate a runtime directory after a successful cleanup.
            return;
        }
        catch (Exception ex)
        {
            session.ExportState = token.IsCancellationRequested ? TwrpExportState.Canceled : TwrpExportState.Failed;
            session.ExportError = token.IsCancellationRequested ? "Twrp_ExportCanceled" : ex is TwrpWorkflowException known ? known.MessageKey : "Twrp_ExportFailed";
            progress?.Report(new(session.Id, session.State, session.ExportError));
        }
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
    }
    private void DeleteCompletedBackupCache(TwrpSession session)
    {
        if (session.Kind != TwrpOperationKind.Backup || session.State != TwrpSessionState.Completed
            || !session.BootRestored || !session.PayloadDownloaded || session.StopRequested)
            throw new TwrpWorkflowException("Twrp_PendingSession");
        CheckCliCache(session);
        string path = session.CacheDirectory!;
        string marker = Path.Combine(path, ".operation"), journal = Path.Combine(path, "session.json");
        if (File.Exists(journal) && File.GetAttributes(journal).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
        string? savedJournal = File.Exists(journal) ? File.ReadAllText(journal) : null;
        try { DeleteCheckedDirectory(path); }
        catch
        {
            // Recursive deletion may remove metadata before encountering a locked
            // file. Keep the remaining owned cache discoverable for export retry.
            if (Directory.Exists(path))
            {
                TwrpArchiveService.CheckDirectory(path);
                if (!File.Exists(marker))
                {
                    using var owner = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    owner.Write(System.Text.Encoding.ASCII.GetBytes(session.Id.ToString("N")));
                    owner.Flush(flushToDisk: true);
                }
                CheckCliCache(session);
                if (savedJournal is not null && !File.Exists(journal)) File.WriteAllText(journal, savedJournal);
            }
            throw;
        }
    }
    private async Task<TwrpSession> RetryCliExportAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        if (session.Kind != TwrpOperationKind.Backup || !session.BootRestored || !session.NativeBackupCompleted || session.StopRequested || session.CleanupCompleted
            || session.State != TwrpSessionState.Completed && !(session.State == TwrpSessionState.Failed && session.WorkflowError is "payload_pull_failed" or "payload_pull_timeout"))
            throw new TwrpWorkflowException("Twrp_InvalidArchive");
        using var operation = Start(session.Device.Serial, token, canCancel: true);
        using var stopCleanup = new StopRegistration(_confirmedStops, operation.OperationId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, operation.CancellationToken);
        if (!session.PayloadDownloaded)
            await root.ExecuteAsRootAsync(session.Device.Serial, async ct => await DownloadCliCacheAsync(session, progress, ct).ConfigureAwait(false), lifetime.Token).ConfigureAwait(false);
        session.State = TwrpSessionState.Completed; // Native completed; only the pull/export is being retried.
        session.WorkflowError = null;
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        await ExportCliAsync(session, progress, lifetime.Token).ConfigureAwait(false);
        return session;
    }
}
