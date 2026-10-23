using System.IO;
using System.IO.Compression;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpArchiveService
{
    internal static IEnumerable<TwrpArchivePayload> NativePayloads(TwrpBackupManifest manifest) =>
        manifest.Version is 3 or 4 ? manifest.Payload.Skip(1) : manifest.Payload;
    private static async Task<TwrpBackupManifest> ReadCliRestoreCoreAsync(ZipArchive zip, TwrpBackupManifest m, CancellationToken token)
    {
        if (m.BackupId == Guid.Empty || string.IsNullOrWhiteSpace(m.Name) || m.Device is null || m.Options is null
            || m.Device.Partitions is null || m.Partitions is null || !m.Partitions.SequenceEqual(new[] { "data" })
            || m.Payload is null || m.Payload.Length < (m.Version == 5 ? 2 : 3) || m.NativeBoot
            || zip.Entries.Count != m.Payload.Length + (m.Version == 3 ? 2 : 1)) throw Invalid();
        if (TwrpSafety.CliBackupName(m.Name + ".zip") != m.Name) throw Invalid();
        TwrpSafety.ValidateSerial(m.Device.Serial);
        TwrpSafety.Slot(m.Device.Slot);
        if (!TwrpRecoveryImage.Available.Any(i => i.Device == m.Device.Codename)
            || m.Device.Partitions.Any(p => p is null || p.Id is null)
            || m.Device.Partitions.Select(p => p.Id).Distinct().Count() != m.Device.Partitions.Count
            || !m.Device.Partitions.Any(p => p.Id == "data" && p.MountPoint == "/data")) throw Invalid();
        if (m.Version == 3) await ValidateTypeAsync(zip, m, required: true, token).ConfigureAwait(false);
        else if (zip.GetEntry("type.json") is not null) throw Invalid();
        if (m.Payload.Any(p => p is null || p.Path is null || p.Bytes < 0)
            || m.Payload.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Payload.Length) throw Invalid();
        if (m.Version is 3 or 4 && (m.Payload[0].Path != "original.img" || m.Payload[0].PartitionId is not null || m.Payload[0].Bytes == 0)) throw Invalid();
        foreach (var file in NativePayloads(m))
        {
            string prefix = m.Name + "/";
            if (!file.Path.StartsWith(prefix, StringComparison.Ordinal) || !SafeFile(file.Path[prefix.Length..])) throw Invalid();
            string name = file.Path[prefix.Length..];
            if (name == "recovery.log" ? file.PartitionId is not null || file.Bytes == 0
                : file.PartitionId != "data" || !name.StartsWith("data.", StringComparison.Ordinal)) throw Invalid();
        }
        foreach (var file in m.Payload)
        {
            var entry = zip.GetEntry(file.Path);
            if (entry is null || entry.Length != file.Bytes || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw Invalid();
        }
        var chunks = m.Payload.Where(p => p.PartitionId == "data" && IsData(p.Path)).OrderBy(p => p.Path, StringComparer.Ordinal).ToArray();
        if (chunks.Length == 0 || chunks.Any(p => p.Bytes == 0) || !m.Payload.Any(p => p.Path == m.Name + "/recovery.log")) throw Invalid();
        var split = chunks.Where(p => !p.Path.EndsWith(".win", StringComparison.Ordinal)).ToArray();
        if (split.Length != 0)
        {
            if (split.Length != chunks.Length) throw Invalid();
            string prefix = split[0].Path[..^3];
            for (int i = 0; i < split.Length; i++)
                if (split[i].Path != prefix + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)) throw Invalid();
        }
        return m;
    }

    // The caller owns a newly created, operation-marked cache. Native .win files
    // are copied out of the outer ZIP byte for byte, never unpacked themselves.
    public async Task<TwrpBackupManifest> ExtractCliRestoreAsync(string path, string destination, CancellationToken token, bool payloadSubdirectory = false)
    {
        CheckDirectory(destination);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var m = await ReadCoreAsync(zip, token).ConfigureAwait(false);
        if (m.Version is not 3 and not 4 and not 5) throw Invalid();
        // Version 3's type.json is validated on read; restore uses the normalized
        // backup manifest for both versions and never needs a second metadata file.
        foreach (var name in new[] { "backup.json" }.Concat(m.Payload.Select(p => p.Path)))
        {
            token.ThrowIfCancellationRequested();
            string root = payloadSubdirectory && name != "backup.json" ? Path.Combine(destination, "payload") : destination;
            string target = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            CheckDirectory(Path.GetDirectoryName(target)!);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            await using var entry = zip.GetEntry(name)!.Open();
            await entry.CopyToAsync(output, token).ConfigureAwait(false);
            if (output.Length != zip.GetEntry(name)!.Length) throw Invalid();
        }
        return m;
    }
}
