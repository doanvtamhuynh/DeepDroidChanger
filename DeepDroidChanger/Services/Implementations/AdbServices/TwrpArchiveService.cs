using System.IO;
using System.IO.Compression;
using System.Text.Json;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

/// <summary>Portable ZIP64 container for native TWRP payload.</summary>
public sealed partial class TwrpArchiveService
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Backup");
    public string EnsureDefaultDirectory()
    {
        CheckDirectory(DefaultDirectory);
        Directory.CreateDirectory(DefaultDirectory);
        return DefaultDirectory;
    }
    public string DefaultName(string device, string serial) => $"{device}_{serial}_{DateTime.Now:ddMMyyyy_HHmm}.zip";
    public string OutputPath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.TrimEnd(' ', '.') != name || name is "." or "..") throw Invalid();
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";
        if (name.Length > 240 || name[..^4].Length == 0) throw Invalid();
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || System.Text.RegularExpressions.Regex.IsMatch(stem, "^(COM|LPT)[0-9]$")) throw Invalid();
        string path = Path.GetFullPath(Path.Combine(directory, name));
        CheckDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) || Directory.Exists(path)) throw new TwrpWorkflowException("Twrp_ArchiveExists");
        return path;
    }
    internal void PrepareOutput(string path)
    {
        OutputPath(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string probe = path + "." + Guid.NewGuid().ToString("N") + ".probe";
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    }
    internal static void CheckDirectory(string directory)
        => TwrpSafety.ValidateLocalDirectory(directory, "Twrp_InvalidArchive");
    private static TwrpWorkflowException Invalid() => new("Twrp_InvalidArchive");
    private static async Task<bool> ValidateTypeAsync(ZipArchive zip, TwrpBackupManifest manifest, bool required, CancellationToken token)
    {
        var entry = zip.GetEntry("type.json");
        if (entry is null)
        {
            if (required) throw Invalid();
            return false; // Older archives retain their backup.json device compatibility checks.
        }
        if (entry.Length is <= 0 or > 65536 || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw Invalid();
        await using var stream = entry.Open();
        var metadata = await JsonSerializer.DeserializeAsync<TwrpBackupType>(stream, TwrpArchiveService.JsonOptions, token).ConfigureAwait(false);
        if (metadata is null || metadata.Version != 1 || metadata.BackupId != manifest.BackupId || metadata.Name != manifest.Name
            || metadata.CreatedAt != manifest.CreatedAt || metadata.DeviceMarker != manifest.Device.Codename
            || metadata.Serial != manifest.Device.Serial || metadata.Slot != manifest.Device.Slot
            || metadata.RecoveryDevice != manifest.Device.Codename || metadata.Partitions is null
            || !metadata.Partitions.SequenceEqual(manifest.Partitions)) throw Invalid();
        return true;
    }
    private static bool SafeFile(string name) => name.Length is > 0 and <= 255 && name is not "." and not ".." && TwrpSafety.SafeName().IsMatch(name);
    private static bool IsData(string name) => System.Text.RegularExpressions.Regex.IsMatch(name, @"\.win(?:[0-9]{3})?\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    internal static string? PartitionFor(TwrpDeviceInfo device, IEnumerable<string> partitions, string name) =>
        partitions.FirstOrDefault(id => name.StartsWith(device.Partitions.Single(p => p.Id == id).MountPoint.TrimStart('/') + ".", StringComparison.Ordinal));
    private static bool DigestFor(TwrpBackupManifest manifest, TwrpArchivePayload file) =>
        manifest.Payload.Any(p => p.Bytes > 0 && (p.Path == file.Path + ".sha2" || p.Path == file.Path + ".md5"));
    public bool HasDigests(TwrpBackupManifest manifest, IReadOnlyList<string> selection)
    {
        var files = manifest.Payload.Where(p => p.PartitionId is not null && selection.Contains(p.PartitionId) && IsData(p.Path)).ToArray();
        return files.Length > 0 && files.All(p => DigestFor(manifest, p));
    }
    public void ValidateSelection(TwrpBackupManifest manifest, IReadOnlyList<string> selection, bool verifyDigest)
    {
        TwrpSafety.ValidateRequestedSelection(selection);
        TwrpSafety.ValidateSelection(manifest.Device, selection);
        if (selection.Any(id => !manifest.Partitions.Contains(id))) throw Invalid();
        if (verifyDigest && !HasDigests(manifest, selection)) throw new TwrpWorkflowException("Twrp_DigestMissing");
    }
    public async Task<TwrpBackupManifest> ReadAsync(string path, CancellationToken token)
    {
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            return await ReadCoreAsync(zip, token).ConfigureAwait(false);
        }
        catch (TwrpWorkflowException ex) when (ex.MessageKey == "Twrp_MediaUnsupported") { throw; }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or OverflowException or InvalidOperationException)
        { throw Invalid(); }
    }
    private static async Task<TwrpBackupManifest> ReadCoreAsync(ZipArchive zip, CancellationToken token)
    {
        if (zip.Entries.Count is < 2 or > 500001 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count) throw Invalid();
        var json = zip.GetEntry("backup.json");
        if (json is null || json.Length > 32 * 1024 * 1024) throw Invalid();
        await using var stream = json.Open();
        var m = await ReadManifestAsync(stream, token).ConfigureAwait(false);
        if (m?.Version is 3 or 4 or 5 or 6) return await ReadCliRestoreCoreAsync(zip, m, token).ConfigureAwait(false);
        if (m is null || m.Version is not 1 and not 2 || m.BackupId == Guid.Empty || string.IsNullOrWhiteSpace(m.Name) || m.Device is null || m.Options is null
            || m.Device.Codename != "sargo" || m.Device.Partitions is null || m.Partitions is null || m.Payload is null || m.Payload.Length == 0) throw Invalid();
        if (m.Payload.Any(p => p is null || p.Path is null) || m.Device.Partitions.Any(p => p is null || p.Id is null) || m.Partitions.Any(p => p is null)) throw Invalid();
        TwrpSafety.ValidateSerial(m.Device.Serial);
        TwrpSafety.Slot(m.Device.Slot);
        string[] known = ["data", "boot", "super", "efs1", "efs2", "fsg", "modem", "persist"];
        if (m.Partitions.Contains("media") || m.Payload.Any(p => p.PartitionId == "media" || p.Path.StartsWith("media/", StringComparison.Ordinal)))
            throw new TwrpWorkflowException("Twrp_MediaUnsupported");
        if (m.Version == 1 && m.NativeBoot) throw Invalid();
        if (m.Device.Partitions.Select(p => p.Id).Distinct().Count() != m.Device.Partitions.Count || m.Partitions.Any(id => !known.Contains(id))) throw Invalid();
        // Older native-only archives may list Media in the device inventory.
        // Accept that metadata without accepting Media as a payload/selection.
        foreach (var part in m.Device.Partitions)
        {
            string? expected = part.Id switch { "data" => "/data", "boot" => "/boot", "super" => "/super", "persist" => "/persist_image", "media" => "/data/media", "efs1" or "efs2" or "fsg" or "modem" => "/ddc_" + part.Id, _ => null };
            if (part.MountPoint != expected || expected is null || part.Sensitive != (part.Id is not "data" and not "super" and not "boot" and not "media")) throw Invalid();
        }
        TwrpSafety.ValidateSelection(m.Device, m.Partitions, allowOriginalBoot: true);
        bool hasType = await ValidateTypeAsync(zip, m, required: false, token).ConfigureAwait(false);
        if (m.Payload.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Payload.Length || zip.Entries.Count != m.Payload.Length + (hasType ? 2 : 1)) throw Invalid();
        if (m.Payload.Select(p => p.Path.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Payload.Length) throw Invalid();
        foreach (var file in m.Payload)
        {
            if (file.Bytes < 0) throw Invalid();
            if (!file.Path.StartsWith("payload/", StringComparison.Ordinal) || !SafeFile(file.Path[8..])
                || file.PartitionId != PartitionFor(m.Device, m.Partitions, file.Path[8..])) throw Invalid();
            if (file.PartitionId is null && file.Path[8..] != "recovery.log") throw Invalid();
            var entry = zip.GetEntry(file.Path);
            if (entry is null || entry.Length != file.Bytes || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw Invalid();
        }
        foreach (var id in m.Partitions)
        {
            var chunks = m.Payload.Where(p => p.PartitionId == id && IsData(p.Path)).ToArray();
            if (chunks.Length == 0 || chunks.Any(p => p.Bytes == 0)) throw Invalid();
            var split = chunks.Where(p => !p.Path.EndsWith(".win", StringComparison.Ordinal)).OrderBy(p => p.Path, StringComparer.Ordinal).ToArray();
            if (split.Length != 0)
            {
                if (split.Length != chunks.Length) throw Invalid();
                string prefix = split[0].Path[..^3];
                for (int i = 0; i < split.Length; i++) if (split[i].Path != prefix + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)) throw Invalid();
            }
        }
        return m;
    }
    public async Task<TwrpBackupManifest> ExtractAsync(string path, string destination, IReadOnlyList<string> selection, bool verifyDigest, CancellationToken token)
    {
        CheckDirectory(destination);
        Directory.CreateDirectory(destination);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var m = await ReadCoreAsync(zip, token).ConfigureAwait(false);
        if (m.Version is 3 or 4 or 5 or 6) throw Invalid(); // Data archives use their owned-cache restore workflow.
        ValidateSelection(m, selection, verifyDigest);
        foreach (var file in m.Payload.Where(p => p.PartitionId is not null && selection.Contains(p.PartitionId)))
        {
            token.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, file.Path[8..]);
            CheckDirectory(Path.GetDirectoryName(target)!);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // A fresh operation directory is required, never follow or overwrite links.
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            await using var entry = zip.GetEntry(file.Path)!.Open();
            await entry.CopyToAsync(output, token).ConfigureAwait(false);
            if (output.Length != file.Bytes) throw Invalid();
        }
        return m;
    }
}
