using System.IO;
using System.Text;

namespace DeepDroidChanger.Helpers;

public static class AtomicFileWriter
{
    public static async Task WriteAllTextAsync(
        string path,
        string contents,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string temporaryPath = string.Concat(fullPath, ".tmp");
        try
        {
            await File.WriteAllTextAsync(
                    temporaryPath,
                    contents,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
