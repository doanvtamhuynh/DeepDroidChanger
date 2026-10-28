using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

/// <summary>Verifies bundled boot sources; guarded image rebuilding remains pinned to sargo.</summary>
public sealed class TwrpImageService
{
    private readonly string _assets;
    public TwrpImageService() : this(Path.Combine(AppContext.BaseDirectory, "Assets", "TWRP")) { }
    internal TwrpImageService(string assets) => _assets = assets;
    public string? GetDefaultImagePath(string deviceMarker)
    {
        var image = TwrpRecoveryImage.Available.FirstOrDefault(item => item.Device == deviceMarker);
        if (image is null) return null;
        string path = Path.Combine(_assets, image.FileName);
        return File.Exists(path) ? path : null;
    }
    private string SourcePath(string? path) => string.IsNullOrWhiteSpace(path) ? Path.Combine(_assets, "twrp_sargo.img") : Path.GetFullPath(path);
    // Keep the verified source open until fastboot exits so it cannot be replaced
    // between verification and boot. This does not use the guarded image builder.
    internal async Task<FileStream> OpenBootSourceAsync(string deviceMarker, CancellationToken token, string? sourceImagePath = null)
    {
        string expectedHash = deviceMarker switch
        {
            "sargo" => TwrpSafety.SourceHash,
            "tissot" => "0aeb095960e6f1c6e2ed350ab8cbb8fa289204a8e473708ce32b5e35812f8d73",
            _ => throw new TwrpWorkflowException("Twrp_EntryUnsupportedDevice")
        };
        string path = sourceImagePath is null ? GetDefaultImagePath(deviceMarker) ?? throw new TwrpWorkflowException("Twrp_EntryInvalidImage") : Path.GetFullPath(sourceImagePath);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        try
        {
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != expectedHash)
                throw new TwrpWorkflowException("Twrp_EntryInvalidImage");
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
    public long SourceImageBytes => GetSourceImageBytes(null);
    public long GetSourceImageBytes(string? path) => new FileInfo(SourcePath(path)).Length;
    public async Task VerifySourceAsync(CancellationToken token, string? sourceImagePath = null)
    {
        using var stream = File.OpenRead(SourcePath(sourceImagePath));
        if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != TwrpSafety.SourceHash)
            throw new TwrpWorkflowException("Twrp_InvalidImage");
    }
    public async Task<string> BuildAsync(TwrpSession session, string destination, CancellationToken token)
    {
        byte[] original = await File.ReadAllBytesAsync(SourcePath(session.SourceImagePath), token).ConfigureAwait(false);
        if (Convert.ToHexStringLower(SHA256.HashData(original)) != TwrpSafety.SourceHash)
            throw new TwrpWorkflowException("Twrp_InvalidImage");
        var files = ReadRamdisk(original);
        Put(files, "system/bin/deepdroid-guard.sh", TwrpScripts.Configuration(session) + await File.ReadAllTextAsync(Path.Combine(_assets, "guard.sh"), token).ConfigureAwait(false), 0x81ed);
        Put(files, "system/bin/deepdroid-record.sh", TwrpScripts.Configuration(session) + await File.ReadAllTextAsync(Path.Combine(_assets, "record.sh"), token).ConfigureAwait(false), 0x81ed);
        string init = Text(files, "init.recovery.service.rc");
        if (init.Contains("ddc_guard", StringComparison.Ordinal)) throw new TwrpWorkflowException("Twrp_InvalidImage");
        Put(files, "init.recovery.service.rc", init + "\non property:ro.twrp.boot=1\n    start ddc_guard\n\nservice ddc_guard /system/bin/sh /system/bin/deepdroid-guard.sh\n    disabled\n    oneshot\n    user root\n    group root\n    seclabel u:r:recovery:s0\n");
        Put(files, "twres/portrait.xml", PatchBridge(Text(files, "twres/portrait.xml")));
        string flags = Text(files, "system/etc/twrp.flags");
        foreach (var part in session.Device.Partitions.Where(item => item.Sensitive))
        {
            if (part.Id == "persist") continue; // Already provided as /persist_image by this exact image.
            if (!TwrpSafety.SafeName().IsMatch(part.BlockName) || !TwrpSafety.SafeName().IsMatch(part.Id)) throw new TwrpWorkflowException("Twrp_InvalidSelection");
            flags += $"\n{part.MountPoint} emmc /dev/block/by-name/{part.BlockName} flags=backup=1;display=\"{part.Id}\"\n";
        }
        Put(files, "system/etc/twrp.flags", flags);
        byte[] image = Rebuild(original, WriteRamdisk(files), session.Device.BootBytes);
        VerifyGenerated(original, image);
        token.ThrowIfCancellationRequested();
        await File.WriteAllBytesAsync(destination, image, token).ConfigureAwait(false);
        return Convert.ToHexStringLower(SHA256.HashData(image));
    }
    internal static string PatchBridge(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var page = document.Descendants("page").Single(item => (string?)item.Attribute("name") == "singleaction_page");
        int count = 0;
        foreach (var action in page.Descendants("action").Where(item => (string?)item.Attribute("function") == "%tw_action%").ToArray())
        {
            // Run after twcmd on the SAME action thread; recursively invoking the CLI from
            // an ORS cmd action would deadlock the TWRP command listener.
            action.AddAfterSelf(
                Action("set", "ddc_pending_function=ddc_function"), Action("set", "ddc_pending_arg=ddc_arg"),
                Action("set", "ddc_function=set"), Action("set", "ddc_arg=ddc_idle=1"),
                Action("%ddc_pending_function%", "%ddc_pending_arg%"),
                Action("cmd", "/system/bin/sh /system/bin/deepdroid-record.sh %ddc_phase% %tw_operation_status% %tw_cancel_backup% e%tw_is_encrypted% d%tw_is_decrypted% b%tw_backup_data_size% p%tw_partition_is_present% s%tw_partition_size% m%tw_partition_mount_point% f%tw_partition_file_system%"),
                Action("set", "ddc_phase=idle"));
            count++;
        }
        if (count != 2) throw new TwrpWorkflowException("Twrp_InvalidImage");
        foreach (var action in page.Elements("action").Where(item => item.Elements("condition").Any(c => (string?)c.Attribute("var1") == "tw_operation_state")))
            action.AddFirst(new XElement("condition", new XAttribute("var1", "ddc_bridge_active"), new XAttribute("var2", "0")));
        // Variables live in ui.xml, but explicit defaults in the on-load actions are not
        // suitable: those would reset the pending operation on every CLI page switch.
        return document.ToString(SaveOptions.DisableFormatting);
    }
    private static XElement Action(string function, string arg) => new("action", new XAttribute("function", function), arg);
    private static string Text(List<CpioEntry> files, string name) => Encoding.UTF8.GetString(files.Single(item => item.Name == name).Data);
    private static void Put(List<CpioEntry> files, string name, string value, uint? mode = null)
    {
        int index = files.FindIndex(item => item.Name == name);
        byte[] bytes = Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (index >= 0) files[index] = files[index] with { Data = bytes };
        else files.Add(new(name, bytes, [100000u + (uint)files.Count, mode ?? 0x81a4u, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0]));
    }
    internal sealed record CpioEntry(string Name, byte[] Data, uint[] Fields);
    internal static List<CpioEntry> ReadRamdisk(byte[] image)
    {
        Layout layout = Parse(image);
        using var gzip = new GZipStream(new MemoryStream(image, layout.RamdiskOffset, layout.RamdiskSize), CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = gzip.Read(buffer)) != 0)
        {
            if (output.Length + read > 256 * 1024 * 1024) throw new TwrpWorkflowException("Twrp_InvalidImage");
            output.Write(buffer, 0, read);
        }
        byte[] archive = output.ToArray();
        var entries = new List<CpioEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int position = 0;
        while (position + 110 <= archive.Length)
        {
            if (!archive.AsSpan(position, 6).SequenceEqual("070701"u8)) throw new TwrpWorkflowException("Twrp_InvalidImage");
            uint[] fields = Enumerable.Range(0, 13).Select(i => uint.Parse(Encoding.ASCII.GetString(archive, position + 6 + i * 8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            int namesize = checked((int)fields[11]), size = checked((int)fields[6]);
            if (namesize < 1 || namesize > 4096 || position + 110L + namesize > archive.Length
                || archive[position + 110 + namesize - 1] != 0) throw new TwrpWorkflowException("Twrp_InvalidImage");
            string name = Encoding.UTF8.GetString(archive, position + 110, namesize - 1);
            int start = Align(checked(position + 110 + namesize), 4);
            if (start + (long)size > archive.Length || name.Contains('\0') || name.Split('/').Contains("..") || name.StartsWith('/') || !seen.Add(name)) throw new TwrpWorkflowException("Twrp_InvalidImage");
            if (name == "TRAILER!!!") return entries;
            entries.Add(new(name, archive.AsSpan(start, size).ToArray(), fields));
            position = Align(checked(start + size), 4);
        }
        throw new TwrpWorkflowException("Twrp_InvalidImage");
    }
    private static byte[] WriteRamdisk(List<CpioEntry> files)
    {
        // Add defaults to the existing variable table once, not on each page entry.
        string ui = Text(files, "twres/ui.xml");
        var document = XDocument.Parse(ui, LoadOptions.PreserveWhitespace);
        var variables = document.Descendants("variables").First();
        foreach ((string name, string value) in new[] { ("ddc_function", "set"), ("ddc_arg", "ddc_idle=1"), ("ddc_phase", "idle"), ("ddc_bridge_active", "0") })
            variables.Add(new XElement("variable", new XAttribute("name", name), new XAttribute("value", value)));
        Put(files, "twres/ui.xml", document.ToString(SaveOptions.DisableFormatting));
        using var archive = new MemoryStream();
        foreach (var file in files.Append(new CpioEntry("TRAILER!!!", [], new uint[13])))
        {
            uint[] fields = (uint[])file.Fields.Clone();
            byte[] name = Encoding.UTF8.GetBytes(file.Name + "\0");
            fields[6] = (uint)file.Data.Length; fields[11] = (uint)name.Length;
            byte[] header = Encoding.ASCII.GetBytes("070701" + string.Concat(fields.Select(value => value.ToString("x8", CultureInfo.InvariantCulture))));
            archive.Write(header); archive.Write(name); Pad(archive, 4); archive.Write(file.Data); Pad(archive, 4);
        }
        Pad(archive, 512);
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(archive.ToArray());
        return compressed.ToArray();
    }
    internal sealed record Layout(int Page, int KernelSize, int RamdiskSize, int DtbSize, int RamdiskOffset, int DtbOffset, int VbmetaOffset, int VbmetaSize);
    internal static Layout Parse(byte[] image)
    {
        try
        {
            if (image.Length < 4096 || !image.AsSpan(0, 8).SequenceEqual("ANDROID!"u8) || U32(image, 40) != 2 || U32(image, 1644) != 1660 || U32(image, 1632) != 0 || U32(image, 24) != 0)
                throw new TwrpWorkflowException("Twrp_InvalidImage");
            int page = checked((int)U32(image, 36)), kernel = checked((int)U32(image, 8)), ramdisk = checked((int)U32(image, 16)), dtb = checked((int)U32(image, 1648));
            if (page != 4096 || kernel <= 0 || ramdisk <= 0 || dtb <= 0) throw new TwrpWorkflowException("Twrp_InvalidImage");
            int ramdiskOffset = checked(page + Align(kernel, page)), dtbOffset = checked(ramdiskOffset + Align(ramdisk, page));
            int footer = image.Length - 64;
            if (dtbOffset + (long)dtb > footer || !image.AsSpan(footer, 4).SequenceEqual("AVBf"u8) || B32(image, footer + 4) != 1)
                throw new TwrpWorkflowException("Twrp_InvalidImage");
            int vbmeta = checked((int)B64(image, footer + 20)), vbmetaSize = checked((int)B64(image, footer + 28));
            if (vbmeta < dtbOffset + dtb || B64(image, footer + 12) != (ulong)vbmeta || vbmetaSize < 256 || vbmeta + (long)vbmetaSize > footer || !image.AsSpan(vbmeta, 4).SequenceEqual("AVB0"u8) || B32(image, vbmeta + 28) != 0 || B64(image, vbmeta + 12) != 0)
                throw new TwrpWorkflowException("Twrp_InvalidImage");
            return new(page, kernel, ramdisk, dtb, ramdiskOffset, dtbOffset, vbmeta, vbmetaSize);
        }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException) { throw new TwrpWorkflowException("Twrp_InvalidImage"); }
    }
    private static byte[] Rebuild(byte[] source, byte[] ramdisk, long partitionBytes)
    {
        Layout layout = Parse(source);
        if (partitionBytes is < 4096 or > 128 * 1024 * 1024) throw new TwrpWorkflowException("Twrp_InvalidImage");
        int dtbOffset = checked(layout.RamdiskOffset + Align(ramdisk.Length, layout.Page));
        int vbmetaOffset = checked(dtbOffset + Align(layout.DtbSize, layout.Page));
        if (vbmetaOffset + layout.VbmetaSize + 4096L > partitionBytes) throw new TwrpWorkflowException("Twrp_ImageTooLarge");
        var output = new byte[checked((int)partitionBytes)];
        source.AsSpan(0, layout.RamdiskOffset).CopyTo(output);
        ramdisk.CopyTo(output, layout.RamdiskOffset);
        source.AsSpan(layout.DtbOffset, layout.DtbSize).CopyTo(output.AsSpan(dtbOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(16), (uint)ramdisk.Length);
        using (var id = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
        {
            void Part(ReadOnlySpan<byte> bytes)
            {
                id.AppendData(bytes); Span<byte> size = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)bytes.Length); id.AppendData(size);
            }
            Part(output.AsSpan(layout.Page, layout.KernelSize)); Part(ramdisk); Part([]); Part([]); Part(output.AsSpan(dtbOffset, layout.DtbSize));
            output.AsSpan(576, 32).Clear(); id.GetHashAndReset().CopyTo(output, 576);
        }
        byte[] vbmeta = source.AsSpan(layout.VbmetaOffset, layout.VbmetaSize).ToArray();
        UpdateAvb(vbmeta, output.AsSpan(0, vbmetaOffset));
        vbmeta.CopyTo(output, vbmetaOffset);
        source.AsSpan(source.Length - 64, 64).CopyTo(output.AsSpan(output.Length - 64));
        int footer = output.Length - 64;
        BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(footer + 12), (ulong)vbmetaOffset);
        BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(footer + 20), (ulong)vbmetaOffset);
        BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(footer + 28), (ulong)vbmeta.Length);
        return output;
    }
    private static void UpdateAvb(byte[] meta, ReadOnlySpan<byte> payload)
    {
        int start = checked(256 + (int)B64(meta, 96)), end = checked(start + (int)B64(meta, 104));
        if (end > meta.Length) throw new TwrpWorkflowException("Twrp_InvalidImage");
        int hashes = 0;
        for (int offset = start; offset < end;)
        {
            ulong tag = B64(meta, offset); int bytes = checked(16 + (int)B64(meta, offset + 8));
            if (bytes < 16 || offset + bytes > end) throw new TwrpWorkflowException("Twrp_InvalidImage");
            if (tag == 2)
            {
                int name = checked((int)B32(meta, offset + 56)), salt = checked((int)B32(meta, offset + 60)), digest = checked((int)B32(meta, offset + 64));
                int body = offset + 132;
                if (name != 4 || digest != 32 || body + name + salt + digest > offset + bytes
                    || Encoding.ASCII.GetString(meta, body, name) != "boot" || Encoding.ASCII.GetString(meta, offset + 24, 6) != "sha256")
                    throw new TwrpWorkflowException("Twrp_InvalidImage");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(meta.AsSpan(body + name, salt)); hash.AppendData(payload);
                hash.GetHashAndReset().CopyTo(meta, body + name + salt);
                BinaryPrimitives.WriteUInt64BigEndian(meta.AsSpan(offset + 16), (ulong)payload.Length);
                hashes++;
            }
            offset += bytes;
        }
        if (hashes != 1) throw new TwrpWorkflowException("Twrp_InvalidImage");
    }
    internal static void VerifyGenerated(byte[] source, byte[] image)
    {
        Layout before = Parse(source), after = Parse(image);
        if (!source.AsSpan(before.Page, before.KernelSize).SequenceEqual(image.AsSpan(after.Page, after.KernelSize))
            || !source.AsSpan(before.DtbOffset, before.DtbSize).SequenceEqual(image.AsSpan(after.DtbOffset, after.DtbSize)))
            throw new TwrpWorkflowException("Twrp_InvalidImage");
        byte[] meta = image.AsSpan(after.VbmetaOffset, after.VbmetaSize).ToArray(), expected = (byte[])meta.Clone();
        UpdateAvb(expected, image.AsSpan(0, after.VbmetaOffset));
        if (!meta.AsSpan().SequenceEqual(expected)) throw new TwrpWorkflowException("Twrp_HashMismatch");
        var files = ReadRamdisk(image);
        if (!files.Any(item => item.Name == "system/bin/deepdroid-guard.sh")) throw new TwrpWorkflowException("Twrp_InvalidImage");
    }
    private static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
    private static uint B32(byte[] b, int p) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p));
    private static ulong B64(byte[] b, int p) => BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(p));
    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);
    private static void Pad(MemoryStream stream, int alignment) { while (stream.Length % alignment != 0) stream.WriteByte(0); }
}
