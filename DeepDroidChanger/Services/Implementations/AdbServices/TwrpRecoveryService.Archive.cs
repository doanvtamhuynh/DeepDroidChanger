using System.IO;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private readonly TwrpArchiveService _archives = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _confirmedStops = new();
    private sealed class StopRegistration(System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> stops, Guid id) : IDisposable
    {
        public void Dispose() => stops.TryRemove(id, out _);
    }

    public Task<bool> RequestStopAsync(string serial, Guid operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = coordinator.GetOperation(serial);
        if (current is not { Kind: DeviceActionKind.TwrpRecovery, CanCancel: true } || current.OperationId != operationId)
            return Task.FromResult(false);
        _confirmedStops.TryAdd(operationId, 0);
        bool requested = coordinator.TryRequestCancellation(serial, operationId);
        if (!requested) _confirmedStops.TryRemove(operationId, out _);
        return Task.FromResult(requested);
    }

    private async Task ImportPayloadAsync(TwrpSession session, string archivePath, TwrpBackupManifest expected,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        progress?.Report(new(session.Id, session.State, "Twrp_Extracting"));
        string directory = Path.Combine(store.DirectoryFor(session.Id), "Import");
        var actual = await _archives.ExtractAsync(archivePath, directory, session.Partitions, session.Options.VerifyDigest, token).ConfigureAwait(false);
        if (actual.BackupId != expected.BackupId || !TwrpSafety.Compatible(actual.Device, session.Device)
            || actual.NativeBoot != expected.NativeBoot || !actual.Partitions.SequenceEqual(expected.Partitions) || !actual.Payload.SequenceEqual(expected.Payload))
            throw new TwrpWorkflowException("Twrp_InvalidArchive");
        progress?.Report(new(session.Id, session.State, "Twrp_Uploading"));
        await root.ExecuteAsRootAsync(session.Device.Serial, async ct =>
        {
            string remote = session.NativeFolder;
            await ShellAsync(session.Device.Serial, $"set -eu\ntest ! -e {TwrpSafety.Quote(remote)}\nmkdir -p {TwrpSafety.Quote(remote)}\ntest \"$(readlink -f {TwrpSafety.Quote(remote)})\" = {TwrpSafety.Quote(remote)}", ct).ConfigureAwait(false);
            foreach (var file in actual.Payload.Where(p => p.PartitionId is not null && session.Partitions.Contains(p.PartitionId)))
                await PayloadTransferAsync(session.Device.Serial, Path.Combine(directory, file.Path[8..]), remote + "/" + file.Path[8..], true, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    private async Task PayloadTransferAsync(string serial, string from, string to, bool push, CancellationToken token)
    {
        // Payloads can be several GB; the boot image's two-minute limit does not apply.
        var result = push ? await adb.PushFileAsync(serial, from, to, token).ConfigureAwait(false)
            : await adb.PullFileAsync(serial, from, to, token).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new TwrpWorkflowException("Twrp_TransferFailed");
    }

    private async Task ExportAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        try
        {
            session.ExportError = null;
            if (session.Artifacts.Length > 0) ParseArtifacts(string.Join("\n", session.Artifacts.Select(f => $"{f.Name}|{f.Bytes}|")));
            ValidatePayload(session.Device, session.Partitions, session.Artifacts);
            string directory = Path.Combine(store.DirectoryFor(session.Id), "Payload-PC");
            TwrpArchiveService.CheckDirectory(directory);
            Directory.CreateDirectory(directory);
            PrepareOriginalBootPayload(session);
            session.ExportState = TwrpExportState.Downloading;
            await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            progress?.Report(new(session.Id, session.State, "Twrp_Downloading"));
            if (!HasCompletePcPayload(session)) await root.ExecuteAsRootAsync(session.Device.Serial, async ct =>
            {
                foreach (var file in session.Artifacts)
                {
                    ct.ThrowIfCancellationRequested();
                    string local = Path.Combine(directory, file.Name);
                    if (File.Exists(local))
                    {
                        if (File.GetAttributes(local).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
                        // Only a completed atomic download may be reused on retry.
                        if (new FileInfo(local).Length == file.Bytes) continue;
                    }
                    string partial = local + ".partial";
                    if (File.Exists(partial) && File.GetAttributes(partial).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
                    await PayloadTransferAsync(session.Device.Serial, session.NativeFolder + "/" + file.Name, partial, false, ct).ConfigureAwait(false);
                    if (new FileInfo(partial).Length != file.Bytes) throw new TwrpWorkflowException("Twrp_TransferFailed");
                    File.Move(partial, local, overwrite: true);
                }
            }, token).ConfigureAwait(false);
            session.ExportState = TwrpExportState.Packaging;
            await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            progress?.Report(new(session.Id, session.State, "Twrp_Packaging"));
            await _archives.WriteAsync(session, directory, token).ConfigureAwait(false);
            session.ExportState = TwrpExportState.Completed;
            progress?.Report(new(session.Id, session.State, "Twrp_ExportDone", session.ArchivePath));
        }
        catch (Exception ex)
        {
            session.ExportState = token.IsCancellationRequested ? TwrpExportState.Canceled : TwrpExportState.Failed;
            session.ExportError = token.IsCancellationRequested ? "Twrp_ExportCanceled" : ex is TwrpWorkflowException known ? known.MessageKey : "Twrp_ExportFailed";
            progress?.Report(new(session.Id, session.State, session.ExportError));
        }
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
    }

    private bool HasCompletePcPayload(TwrpSession session)
    {
        if (session.Artifacts.Length > 0) ParseArtifacts(string.Join("\n", session.Artifacts.Select(f => $"{f.Name}|{f.Bytes}|")));
        string directory = Path.Combine(store.DirectoryFor(session.Id), "Payload-PC");
        TwrpArchiveService.CheckDirectory(directory);
        return session.Artifacts.All(file =>
        {
            string path = Path.Combine(directory, file.Name);
            if (!File.Exists(path)) return false;
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
            return new FileInfo(path).Length == file.Bytes;
        });
    }

    private TwrpArtifact OriginalBootArtifact(TwrpSession session) => new("boot.emmc.win", new FileInfo(store.FileFor(session.Id, "original.img")).Length);

    private void PrepareOriginalBootPayload(TwrpSession session)
    {
        if (!session.Partitions.Contains("boot") || session.NativeBoot) return;
        string directory = Path.Combine(store.DirectoryFor(session.Id), "Payload-PC");
        TwrpArchiveService.CheckDirectory(directory);
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "boot.emmc.win");
        if (File.Exists(target))
        {
            if (File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
            if (new FileInfo(target).Length == session.Artifacts.Single(a => a.Name == "boot.emmc.win").Bytes) return;
        }
        string partial = target + ".partial";
        if (File.Exists(partial) && File.GetAttributes(partial).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
        File.Copy(store.FileFor(session.Id, "original.img"), partial, overwrite: true);
        File.Move(partial, target, overwrite: true);
    }

    public async Task<TwrpSession> RetryExportAsync(Guid sessionId, IProgress<TwrpProgress>? progress, CancellationToken cancellationToken)
    {
        var session = await store.LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session.CliBackup) return await RetryCliExportAsync(session, progress, cancellationToken).ConfigureAwait(false);
        if (session.Partitions.Contains("media")) throw new TwrpWorkflowException("Twrp_MediaUnsupported");
        if (session.Kind != TwrpOperationKind.Backup || session.State != TwrpSessionState.Completed || !session.BootRestored
            || session.ArchivePath is null || session.ExportState == TwrpExportState.Completed || session.Artifacts.Length == 0)
            throw new TwrpWorkflowException("Twrp_InvalidArchive");
        using var operation = Start(session.Device.Serial, cancellationToken, canCancel: true);
        using var stopCleanup = new StopRegistration(_confirmedStops, operation.OperationId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.CancellationToken);
        session.StopRequested = false;
        ValidatePayload(session.Device, session.Partitions, session.Artifacts);
        PrepareOriginalBootPayload(session);
        if (File.Exists(session.ArchivePath))
        {
            // Reconcile a crash after atomic ZIP publication but before journal save.
            var published = await _archives.ReadAsync(session.ArchivePath, lifetime.Token).ConfigureAwait(false);
            if (published.BackupId != session.Id || !TwrpSafety.Compatible(published.Device, session.Device) || !published.Partitions.SequenceEqual(session.Partitions)
                || published.NativeBoot != session.NativeBoot || !published.Payload.SequenceEqual(TwrpArchiveService.Payloads(session)))
                throw new TwrpWorkflowException("Twrp_ArchiveExists");
            session.ExportState = TwrpExportState.Completed; session.ExportError = null;
            await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            progress?.Report(new(session.Id, session.State, "Twrp_ExportDone", session.ArchivePath));
            return session;
        }
        if (!HasCompletePcPayload(session))
        {
            var actual = await InspectCoreAsync(session.Device.Serial, lifetime.Token).ConfigureAwait(false);
            if (actual.Device is null || !actual.CanStart || !TwrpSafety.Compatible(session.Device, actual.Device)) throw new TwrpWorkflowException("Twrp_IncompatibleBackup");
        }
        string expected = TwrpSafety.Storage(session.StorageRoot) + "/DeepDroidChanger/TWRP/" + session.Id.ToString("N");
        if (session.NativeFolder != expected) throw new TwrpWorkflowException("Twrp_UnsafePath");
        _archives.OutputPath(Path.GetDirectoryName(session.ArchivePath)!, Path.GetFileName(session.ArchivePath));
        await ExportAsync(session, progress, lifetime.Token).ConfigureAwait(false);
        return session;
    }
}
