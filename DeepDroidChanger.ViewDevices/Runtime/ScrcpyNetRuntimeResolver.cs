using System.IO;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetRuntimeResolver(string applicationBaseDirectory, string canonicalAdbPath)
{
    private static readonly string[] RequiredRuntimeFiles =
    [
        "scrcpy-server.jar",
        "avcodec-59.dll",
        "avformat-59.dll",
        "avutil-57.dll",
        "swresample-4.dll",
        "swscale-6.dll"
    ];

    public ScrcpyNetRuntimeInfo Resolve()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Single View requires the Windows ScrcpyNet runtime.");
        if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "Single View requires a 64-bit Windows process.");
        }

        string runtimeDirectory = Path.GetFullPath(
            Path.Combine(applicationBaseDirectory, "Assets", "Tools"));
        if (!Directory.Exists(runtimeDirectory))
            throw new DirectoryNotFoundException(
                $"The ScrcpyNet runtime was not found at {runtimeDirectory}.");

        string adbPath = Path.GetFullPath(canonicalAdbPath);
        if (!File.Exists(adbPath))
            throw new FileNotFoundException(
                "The canonical DeepDroidChanger ADB executable was not found.",
                adbPath);

        foreach (string fileName in RequiredRuntimeFiles)
        {
            string requiredPath = Path.Combine(runtimeDirectory, fileName);
            if (!File.Exists(requiredPath))
            {
                throw new FileNotFoundException(
                    $"The ScrcpyNet runtime is incomplete: {fileName} is missing.",
                    requiredPath);
            }
        }

        return new ScrcpyNetRuntimeInfo(
            runtimeDirectory,
            Path.Combine(runtimeDirectory, "scrcpy-server.jar"),
            adbPath);
    }
}
