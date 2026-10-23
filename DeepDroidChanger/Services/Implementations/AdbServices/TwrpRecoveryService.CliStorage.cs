using System.IO;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private string CliCachePath(TwrpSession session) => Path.GetFullPath(Path.Combine(store.CacheRoot,
        TwrpSafety.CliBackupName(session.ArchivePath!) + (session.CliRestore ? "_restore_" + session.Id.ToString("N") : "")));

    private static string CliPayloadDirectory(TwrpSession session) => session.CacheLayoutVersion == 1
        ? Path.Combine(session.CacheDirectory!, "payload") : session.CacheDirectory!;

    private static string CliDevicePath(TwrpSession session) => "/data/media/0/TWRP/BACKUPS/" + session.Device.Serial + "/" + TwrpSafety.CliBackupName(session.ArchivePath!);

    private void CheckCliCache(TwrpSession session)
    {
        string expected = CliCachePath(session);
        // Keep unfinished sessions at their recorded location. New caches are
        // always created under the executable's Backup/cache directory.
        string legacy = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(session.ArchivePath!)!, "cache", TwrpSafety.CliBackupName(session.ArchivePath!)));
        string previous = Path.Combine(store.CacheRoot, TwrpSafety.CliBackupName(session.ArchivePath!));
        if (!string.Equals(expected, session.CacheDirectory, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(previous, session.CacheDirectory, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(legacy, session.CacheDirectory, StringComparison.OrdinalIgnoreCase)) throw new TwrpWorkflowException("Twrp_UnsafePath");
        TwrpArchiveService.CheckDirectory(session.CacheDirectory!);
        string marker = Path.Combine(session.CacheDirectory!, ".operation");
        if (!File.Exists(marker) || File.GetAttributes(marker).HasFlag(FileAttributes.ReparsePoint)
            || File.ReadAllText(marker) != session.Id.ToString("N")) throw new TwrpWorkflowException("Twrp_UnsafePath");
    }

    private static void DeleteCheckedDirectory(string path)
    {
        TwrpArchiveService.CheckDirectory(path);
        foreach (string directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            TwrpArchiveService.CheckDirectory(directory);
        if (Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Any(file => File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)))
            throw new TwrpWorkflowException("Twrp_UnsafePath");
        Directory.Delete(path, recursive: true);
    }
}
