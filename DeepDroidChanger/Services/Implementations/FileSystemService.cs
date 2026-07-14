using System.IO;

namespace DeepDroidChanger.Services;

public sealed class FileSystemService : IFileSystemService
{
    public bool FileExists(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }
}
