using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

internal static partial class TwrpSafety
{
    public const string SourceHash = "82133ef23effdc4787bc1f08a7854c96638b5d074421c226c02602630f231665";
    public const string GuardVersion = "sargo-guard-9";
    public const string Confirmation = "FLASH TWRP";
    public static string CliBackupName(string archivePath)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(archivePath);
        // TWRP's CLI tokenizes names on spaces; keep the same single token on PC/device.
        if (name.Length is < 1 or > 64 || name.Contains("..", StringComparison.Ordinal) || !SafeName().IsMatch(name))
            throw new TwrpWorkflowException("Twrp_InvalidBackupName");
        return name;
    }
    public static string HashText(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Quote(string value)
    {
        if (value.Any(character => char.IsControl(character))) throw new TwrpWorkflowException("Twrp_UnsafePath");
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
    public static string Slot(string value) => value.Trim() switch
    {
        "_a" => "_a", "_b" => "_b", _ => throw new TwrpWorkflowException("Twrp_InvalidSlot")
    };
    public static string Boot(string slot) => "/dev/block/by-name/boot" + Slot(slot);
    public static string RemoteRoot(string path)
    {
        if (!RemotePath().IsMatch(path) || path.Contains("..", StringComparison.Ordinal) || path.EndsWith('/') || path.Contains("//", StringComparison.Ordinal))
            throw new TwrpWorkflowException("Twrp_UnsafePath");
        return path;
    }
    public static string Storage(string path)
    {
        RemoteRoot(path);
        if (path is not "/data/media/0" and not "/usb_otg" && !path.StartsWith("/storage/", StringComparison.Ordinal) && !path.StartsWith("/mnt/media_rw/", StringComparison.Ordinal))
            throw new TwrpWorkflowException("Twrp_UnsafePath");
        return path;
    }
    public static string Staging(string path)
    {
        RemoteRoot(path);
        if (path is not "/metadata" and not "/data/adb" and not "/data/local/tmp"
            && !path.StartsWith("/mnt/media_rw/", StringComparison.Ordinal)) throw new TwrpWorkflowException("Twrp_UnsafePath");
        return path;
    }
    internal static void ValidateLocalDirectory(string path, string errorKey = "Twrp_UnsafePath")
    {
        string fullPath = Path.GetFullPath(path);
        for (DirectoryInfo? parent = new(fullPath); parent is not null; parent = parent.Parent)
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new TwrpWorkflowException(errorKey);
    }
    public static void ValidateSerial(string serial)
    {
        if (!SafeName().IsMatch(serial) || serial.Length > 128) throw new TwrpWorkflowException("Twrp_UnsupportedDevice");
    }
    public static bool Compatible(TwrpDeviceInfo source, TwrpDeviceInfo target) =>
        source.Serial == target.Serial && source.Codename == target.Codename && source.Fingerprint == target.Fingerprint
        && source.VendorFingerprint == target.VendorFingerprint && source.AndroidSdk == target.AndroidSdk
        && source.Slot == target.Slot && source.LayoutHash == target.LayoutHash && source.Hardware == target.Hardware;
    public static bool CompatibleBackup(TwrpBackupManifest source, TwrpDeviceInfo target) => source.Version is 4 or 5
        ? source.Device.Serial == target.Serial && source.Device.Codename == target.Codename
            && source.Device.Slot == target.Slot && source.Device.LayoutHash == target.LayoutHash
        : Compatible(source.Device, target);
    public static void ValidateSelection(TwrpDeviceInfo device, IReadOnlyList<string> ids, bool allowOriginalBoot = false)
    {
        if (ids.Count == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new TwrpWorkflowException("Twrp_InvalidSelection");
        foreach (string id in ids)
            if (!device.Partitions.Any(item => item.Id == id) || id == "boot" && !allowOriginalBoot)
                throw new TwrpWorkflowException("Twrp_InvalidSelection");
        if (ids.Contains("super") && ids.Any(id => id is "system" or "vendor")) throw new TwrpWorkflowException("Twrp_InvalidSelection");
    }
    public static void ValidateRequestedSelection(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count
            || ids.Any(id => !TwrpPartitionCatalog.Known.Any(p => p.Id == id)))
            throw new TwrpWorkflowException("Twrp_InvalidSelection");
    }
    public static void Transition(TwrpSession session, TwrpSessionState next)
    {
        if (next is TwrpSessionState.BootRestored or TwrpSessionState.Completed && !session.BootRestored)
            throw new TwrpWorkflowException("Twrp_BootRestoreFailed");
        if (session.IsTerminal || next == TwrpSessionState.Preparing) throw new TwrpWorkflowException("Twrp_InvalidJournal");
        bool allowed = next is TwrpSessionState.Failed or TwrpSessionState.NeedsRecovery or TwrpSessionState.RestoreBootIntent
            || session.Kind == TwrpOperationKind.Backup && session.Partitions.SequenceEqual(new[] { "boot" })
                && session.State == TwrpSessionState.OriginalSaved && next == TwrpSessionState.BootRestored
            || (session.State, next) is
                (TwrpSessionState.Preparing, TwrpSessionState.OriginalSaved) or
                (TwrpSessionState.OriginalSaved, TwrpSessionState.ImagePrepared) or
                (TwrpSessionState.ImagePrepared, TwrpSessionState.FlashIntent) or
                (TwrpSessionState.FlashIntent, TwrpSessionState.TwrpFlashed) or
                (TwrpSessionState.TwrpFlashed, TwrpSessionState.Rebooted) or
                (TwrpSessionState.Rebooted, TwrpSessionState.RecoveryRunning) or
                (TwrpSessionState.RecoveryRunning, TwrpSessionState.BootRestored) or
                (TwrpSessionState.RestoreBootIntent, TwrpSessionState.BootRestored) or
                (TwrpSessionState.NeedsRecovery, TwrpSessionState.BootRestored) or
                (TwrpSessionState.BootRestored, TwrpSessionState.Completed);
        if (!allowed) throw new TwrpWorkflowException("Twrp_InvalidJournal");
        session.State = next;
    }
    [GeneratedRegex("^/[A-Za-z0-9_./-]+\\z", RegexOptions.CultureInvariant)] private static partial Regex RemotePath();
    [GeneratedRegex("^[A-Za-z0-9_.-]+\\z", RegexOptions.CultureInvariant)] internal static partial Regex SafeName();
}
