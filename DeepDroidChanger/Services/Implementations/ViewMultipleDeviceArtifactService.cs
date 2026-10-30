using System.Globalization;
using System.IO;

namespace DeepDroidChanger.Services;

public sealed class ViewMultipleDeviceArtifactService : IViewMultipleDeviceArtifactService
{
    public string CreateRunDirectory(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        string name = $"ViewMultipleDevices-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string directory = Path.Combine(Path.GetFullPath(destinationDirectory), name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public string CreateDevicePath(string runDirectory, int index, string serial, string fileName)
    {
        string directory = Path.Combine(runDirectory,
            $"{index.ToString("D3", CultureInfo.InvariantCulture)}-{SafeName(serial)}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, SafeName(fileName));
    }

    private static string SafeName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new(value.Take(100).Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray());
        clean = clean.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(clean) || clean is "." or "..") clean = "device-file";
        // Prefix also excludes reserved Windows device names such as CON and AUX.
        return "device-" + clean;
    }
}
