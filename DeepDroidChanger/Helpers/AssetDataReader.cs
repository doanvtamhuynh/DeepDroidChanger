using System.IO;
using System.Reflection;
using System.Text;
using DeepDroidChanger.Constants;

namespace DeepDroidChanger.Helpers;

public static class AssetDataReader
{
    public static string ReadText(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string normalizedRelativePath = relativePath
            .Replace('\\', '/')
            .TrimStart('/');
        string[] pathSegments = normalizedRelativePath.Split('/');

        if (!normalizedRelativePath.StartsWith(AssetConstants.Data.RootPath, StringComparison.Ordinal)
            || pathSegments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidOperationException("Asset path must resolve inside the embedded data directory.");
        }

        Assembly assembly = typeof(AssetDataReader).Assembly;
        string resourceName = string.Concat(
            assembly.GetName().Name,
            ".",
            normalizedRelativePath.Replace('/', '.'));
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new FileNotFoundException("Embedded application data asset was not found.", normalizedRelativePath);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
