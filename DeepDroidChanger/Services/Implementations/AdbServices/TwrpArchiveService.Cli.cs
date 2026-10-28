using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpArchiveService
{
    private static TwrpBackupManifest CliManifest(TwrpOperation operation)
    {
        string name = TwrpSafety.CliBackupName(operation.ArchivePath!);
        return new(6, operation.Id, name, operation.CreatedAt, operation.Device, operation.Options, ["data"],
            operation.Artifacts.Select(f => new TwrpArchivePayload(name + "/" + f.Name, f.Bytes, f.Name == "recovery.log" ? null : "data")).ToArray());
    }

    public async Task WriteCliAsync(TwrpOperation operation, CancellationToken token, Action? verifying = null)
    {
        string path = operation.ArchivePath ?? throw Invalid();
        OutputPath(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        var manifest = CliManifest(operation);
        string recoveryDevice = operation.RecoveryDevice ?? operation.Device.Codename;
        string partial = path + "." + operation.Id.ToString("N") + ".partial";
        CheckDirectory(Path.GetDirectoryName(partial)!);
        if (File.Exists(partial) && File.GetAttributes(partial).HasFlag(FileAttributes.ReparsePoint)) throw Invalid();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        string payloadRoot = Path.Combine(operation.CacheDirectory!, "payload");
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in manifest.Payload)
                {
                    token.ThrowIfCancellationRequested();
                    string local = Path.Combine(payloadRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    CheckDirectory(Path.GetDirectoryName(local)!);
                    if (File.GetAttributes(local).HasFlag(FileAttributes.ReparsePoint)) throw Invalid();
                    await using var input = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    if (input.Length != file.Bytes) throw Invalid();
                    await using var entry = zip.CreateEntry(file.Path, CompressionLevel.Fastest).Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    byte[] buffer = new byte[65536];
                    int read;
                    while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await entry.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    }
                    hashes.Add(file.Path, Convert.ToHexStringLower(hash.GetHashAndReset()));
                }
                var payload = manifest.Payload.Select(file => file with { Sha256 = hashes[file.Path] }).ToArray();
                var metadata = new TwrpDataBackupManifest(6, manifest.BackupId, manifest.Name, manifest.CreatedAt,
                    manifest.Device.Codename, manifest.Device.Serial, recoveryDevice, manifest.Partitions, payload);
                await using (var json = zip.CreateEntry("backup.json").Open())
                    await JsonSerializer.SerializeAsync(json, metadata, JsonOptions, token).ConfigureAwait(false);
            }
            await output.FlushAsync(token).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        verifying?.Invoke();
        await VerifyCliCoreAsync(manifest, partial, hashes, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        File.Move(partial, path, overwrite: false);
    }

    private static async Task VerifyCliCoreAsync(TwrpBackupManifest expected, string path,
        IReadOnlyDictionary<string, string> hashes, CancellationToken token)
    {
        CheckDirectory(Path.GetDirectoryName(path)!);
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw Invalid();
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var m = await ReadCoreAsync(zip, token).ConfigureAwait(false);
        if (m.Version != expected.Version || m.BackupId != expected.BackupId
            || m.Name != expected.Name || m.CreatedAt != expected.CreatedAt) throw Invalid();
        if (!NativePayloads(m).Select(file => file with { Sha256 = null }).SequenceEqual(expected.Payload)) throw Invalid();
        foreach (var file in m.Payload)
        {
            var entry = zip.GetEntry(file.Path);
            if (entry is null || entry.Length != file.Bytes || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw Invalid();
            // Read the payload, not just its central-directory length, before cleanup.
            await using var content = entry.Open();
            string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, token).ConfigureAwait(false));
            if (file.Sha256 is not null && hash != file.Sha256) throw Invalid();
            if (!hashes.TryGetValue(file.Path, out string? expectedHash) || hash != expectedHash) throw Invalid();
        }
    }

    private static async Task<TwrpBackupManifest?> ReadManifestAsync(Stream stream, CancellationToken token)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt32(out int value)) throw Invalid();
        if (value == 6)
        {
            var data = document.RootElement.Deserialize<TwrpDataBackupManifest>(TwrpArchiveService.JsonOptions);
            if (data is null || string.IsNullOrWhiteSpace(data.DeviceMarker) || string.IsNullOrWhiteSpace(data.Serial)
                || data.RecoveryDevice != data.DeviceMarker || data.CreatedAt == default) throw Invalid();
            var live = new TwrpDeviceInfo(data.Serial, data.DeviceMarker, "", "", 0, "", "", 0, "", "", 0,
                [new("data", "/data", "", 0)], []);
            return new(6, data.BackupId, data.Name, data.CreatedAt, live, new(false, true), data.Partitions, data.Payload);
        }
        if (value is not 4 and not 5) return document.RootElement.Deserialize<TwrpBackupManifest>(TwrpArchiveService.JsonOptions);
        var m = document.RootElement.Deserialize<TwrpCliBackupManifest>(TwrpArchiveService.JsonOptions);
        if (m is null || string.IsNullOrWhiteSpace(m.DeviceMarker) || string.IsNullOrWhiteSpace(m.Serial)
            || string.IsNullOrWhiteSpace(m.Slot) || string.IsNullOrWhiteSpace(m.LayoutHash)
            || m.RecoveryDevice != m.DeviceMarker || m.CreatedAt == default) throw Invalid();
        // The runtime contract remains shared with legacy archives. Only restore-relevant
        // identity is retained as archive metadata only; it is not compared with a live device.
        var device = new TwrpDeviceInfo(m.Serial, m.DeviceMarker, "", "", 0, m.Slot, "", 0, m.LayoutHash, "", 0,
            [new("data", "/data", "userdata", 0)], []);
        return new(value, m.BackupId, m.Name, m.CreatedAt, device, new(false, true), m.Partitions, m.Payload);
    }
}
