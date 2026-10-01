using System.IO;

namespace DeepDroidChanger.Constants;

public static class BackupPathConstants
{
    public static string DefaultDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Backup");

    public static string EnsureDefaultDirectory()
    {
        string directory = DefaultDirectory;
        try
        {
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }
}
