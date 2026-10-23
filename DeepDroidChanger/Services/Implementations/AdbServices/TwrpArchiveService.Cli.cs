using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpArchiveService
{
    private static TwrpBackupManifest CliManifest(TwrpSession session)
    {
        string name = TwrpSafety.CliBackupName(session.ArchivePath!);
        return new(5, session.Id, name, session.CreatedAt, session.Device, session.Options, ["data"],
            session.Artifacts.Select(f => new TwrpArchivePayload(name + "/" + f.Name, f.Bytes, f.Name == "recovery.log" ? null : "data")).ToArray());
    }

    public async Task WriteCliAsync(TwrpSession session, CancellationToken token)
    {
        string path = session.ArchivePath ?? throw Invalid();
        OutputPath(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        var manifest = CliManifest(session);
        string recoveryDevice = session.RecoveryDevice ?? session.Device.Codename;
        if (recoveryDevice != session.Device.Codename) throw Invalid();
        var metadata = new TwrpCliBackupManifest(5, manifest.BackupId, manifest.Name, manifest.CreatedAt,
            manifest.Device.Codename, manifest.Device.Serial, manifest.Device.Slot, manifest.Device.LayoutHash,
            recoveryDevice, manifest.Partitions, manifest.Payload);
        string metadataPath = Path.Combine(session.CacheDirectory!, "backup.json");
        CheckDirectory(session.CacheDirectory!);
        if (File.Exists(metadataPath) && File.GetAttributes(metadataPath).HasFlag(FileAttributes.ReparsePoint)) throw Invalid();
        await DeepDroidChanger.Helpers.AtomicFileWriter.WriteAllTextAsync(metadataPath,
            JsonSerializer.Serialize(metadata, TwrpSessionStore.JsonOptions), token).ConfigureAwait(false);
        string partial = path + "." + session.Id.ToString("N") + ".partial";
        CheckDirectory(Path.GetDirectoryName(partial)!);
        if (File.Exists(partial) && File.GetAttributes(partial).HasFlag(FileAttributes.ReparsePoint)) throw Invalid();
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using (var json = zip.CreateEntry("backup.json").Open())
                    await JsonSerializer.SerializeAsync(json, metadata, TwrpSessionStore.JsonOptions, token).ConfigureAwait(false);
                foreach (var file in manifest.Payload)
                {
                    token.ThrowIfCancellationRequested();
                    string payloadRoot = session.CacheLayoutVersion == 1 ? Path.Combine(session.CacheDirectory!, "payload") : session.CacheDirectory!;
                    string local = Path.Combine(payloadRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    CheckDirectory(Path.GetDirectoryName(local)!);
                    if (File.GetAttributes(local).HasFlag(FileAttributes.ReparsePoint)) throw Invalid();
                    await using var input = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    if (input.Length != file.Bytes) throw Invalid();
                    await using var entry = zip.CreateEntry(file.Path, CompressionLevel.Fastest).Open();
                    await input.CopyToAsync(entry, token).ConfigureAwait(false);
                }
            }
            await output.FlushAsync(token).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        await VerifyCliAsync(session, partial, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        File.Move(partial, path, overwrite: false);
    }

    public async Task VerifyCliAsync(TwrpSession session, string path, CancellationToken token)
    {
        CheckDirectory(Path.GetDirectoryName(path)!);
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw Invalid();
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var m = await ReadCoreAsync(zip, token).ConfigureAwait(false);
        string name = TwrpSafety.CliBackupName(session.ArchivePath!);
        if (m.Version is not 3 and not 4 and not 5 || m.BackupId != session.Id || m.Name != name || m.CreatedAt != session.CreatedAt
            || !TwrpSafety.CompatibleBackup(m, session.Device)) throw Invalid();
        var expected = session.Artifacts.Select(f => new TwrpArchivePayload(name + "/" + f.Name, f.Bytes, f.Name == "recovery.log" ? null : "data")).ToArray();
        if (!NativePayloads(m).SequenceEqual(expected)) throw Invalid();
        foreach (var file in m.Payload)
        {
            var entry = zip.GetEntry(file.Path);
            if (entry is null || entry.Length != file.Bytes || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw Invalid();
            // Read the payload, not just its central-directory length, before cleanup.
            await using var content = entry.Open();
            byte[] hash = await SHA256.HashDataAsync(content, token).ConfigureAwait(false);
            string payloadRoot = session.CacheLayoutVersion == 1 ? Path.Combine(session.CacheDirectory!, "payload") : session.CacheDirectory!;
            string local = Path.Combine(payloadRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            CheckDirectory(Path.GetDirectoryName(local)!);
            if (File.Exists(local))
            {
                if (File.GetAttributes(local).HasFlag(FileAttributes.ReparsePoint)) throw Invalid();
                await using var source = File.OpenRead(local);
                byte[] sourceHash = await SHA256.HashDataAsync(source, token).ConfigureAwait(false);
                if (source.Length != file.Bytes || !hash.AsSpan().SequenceEqual(sourceHash)) throw Invalid();
            }
        }
    }

    private static async Task<TwrpBackupManifest?> ReadManifestAsync(Stream stream, CancellationToken token)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt32(out int value)) throw Invalid();
        if (value is not 4 and not 5) return document.RootElement.Deserialize<TwrpBackupManifest>(TwrpSessionStore.JsonOptions);
        var m = document.RootElement.Deserialize<TwrpCliBackupManifest>(TwrpSessionStore.JsonOptions);
        if (m is null || string.IsNullOrWhiteSpace(m.DeviceMarker) || string.IsNullOrWhiteSpace(m.Serial)
            || string.IsNullOrWhiteSpace(m.Slot) || string.IsNullOrWhiteSpace(m.LayoutHash)
            || m.RecoveryDevice != m.DeviceMarker || m.CreatedAt == default) throw Invalid();
        // The runtime contract remains shared with legacy archives. Only restore-relevant
        // identity is persisted in version 4/5; live operational paths come from preflight.
        var device = new TwrpDeviceInfo(m.Serial, m.DeviceMarker, "", "", 0, m.Slot, "", 0, m.LayoutHash, "", 0,
            [new("data", "/data", "userdata", 0)], []);
        return new(value, m.BackupId, m.Name, m.CreatedAt, device, new(false, true), m.Partitions, m.Payload);
    }
}
