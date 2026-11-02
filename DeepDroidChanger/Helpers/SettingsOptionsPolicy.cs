using System.IO;
using DeepDroidChanger.Models;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.Helpers;

internal static class SettingsOptionsPolicy
{
    private static string Choice(string? value, string fallback, params string[] choices)
        => choices.Contains(value, StringComparer.Ordinal) ? value! : fallback;

    public static SettingsOptions Normalize(SettingsOptions? value)
    {
        value ??= new();
        return value with
        {
            AccountDisplayName = (value.AccountDisplayName ?? "").Trim(),
            SingleViewQuality = Choice(value.SingleViewQuality, "Balanced", "Lightweight", "Balanced", "Detailed"),
            MultiViewQuality = Choice(value.MultiViewQuality, "Lightweight", "Lightweight", "Balanced", "Detailed"),
            KeyboardMode = Choice(value.KeyboardMode, "Sdk", "Sdk", "Uhid"),
            MaxConcurrentTools = Choice(value.MaxConcurrentTools, "4", "1", "2", "4", "8"),
            BatchDelay = Choice(value.BatchDelay, "0", "0", "250", "500", "1000"),
            ScreenshotDestination = Choice(value.ScreenshotDestination, "Ask", "Ask", "Custom"),
            ExportDestination = Choice(value.ExportDestination, "Ask", "Ask", "Custom"),
            ScreenshotFolder = value.ScreenshotFolder ?? "",
            ExportFolder = value.ExportFolder ?? "",
            BackupFolder = string.IsNullOrWhiteSpace(value.BackupFolder) ? "Backup" : value.BackupFolder,
            Appearance = AppearanceOptionsPolicy.Normalize(value.Appearance)
        };
    }

    public static bool IsDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try
        {
            string full = Path.GetFullPath(path);
            return !path.Any(c => c < 32 || c is '*' or '?' or '"' or '<' or '>' or '|')
                && !full[Path.GetPathRoot(full)!.Length..].Contains(':');
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsValid(SettingsOptions value) =>
        AppearanceOptionsPolicy.IsValid(value.Appearance)
        && (value.ScreenshotDestination != "Custom" || IsDirectoryPath(value.ScreenshotFolder))
        && (value.ExportDestination != "Custom" || IsDirectoryPath(value.ExportFolder))
        && (value.BackupFolder == "Backup" || IsDirectoryPath(value.BackupFolder));

    public static ViewDeviceLaunchOptions LaunchOptions(string serial, SettingsOptions settings, bool multi)
    {
        string quality = multi ? settings.MultiViewQuality : settings.SingleViewQuality;
        return new ViewDeviceLaunchOptions(serial)
        {
            MaxSize = quality switch { "Lightweight" => 720, "Detailed" => 1920, _ => 1280 },
            MaxFps = quality switch { "Lightweight" => 20, "Detailed" => 60, _ => 30 },
            VideoBitRate = quality switch { "Lightweight" => "2M", "Detailed" => "8M", _ => "4M" },
            NoAudio = !settings.EnableAudio,
            KeyboardMode = settings.KeyboardMode == "Uhid" ? ViewDeviceKeyboardMode.Uhid : ViewDeviceKeyboardMode.Sdk
        };
    }
}
