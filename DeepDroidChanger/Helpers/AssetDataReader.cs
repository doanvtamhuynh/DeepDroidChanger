using System.IO;
using System.Text;

namespace DeepDroidChanger.Helpers;

public static class AssetDataReader
{
    public static string ReadText(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, normalizedRelativePath));

        if (!fullPath.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Asset path resolves outside the application directory.");

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Application data asset was not found.", fullPath);

        return File.ReadAllText(fullPath, Encoding.UTF8);
    }
}
