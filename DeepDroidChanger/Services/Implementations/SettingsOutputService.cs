using System.IO;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed class SettingsOutputService(AppSettings settings) : ISettingsOutputService
{
    public string? GetFolder(bool screenshot)
    {
        SettingsOptions value = settings.Options;
        string destination = screenshot ? value.ScreenshotDestination : value.ExportDestination;
        if (destination != "Custom") return null;
        string folder = screenshot ? value.ScreenshotFolder : value.ExportFolder;
        if (!SettingsOptionsPolicy.IsDirectoryPath(folder)) throw new IOException("Invalid output directory.");
        return Path.GetFullPath(folder);
    }

    public string? GetFilePath(bool screenshot, string suggestedName)
    {
        string? folder = GetFolder(screenshot);
        if (folder is null) return null;
        Directory.CreateDirectory(folder);
        string name = Path.GetFileName(suggestedName);
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        // A unique name avoids overwriting an earlier capture or export.
        return Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}_{Guid.NewGuid():N}{Path.GetExtension(name)}");
    }
}
