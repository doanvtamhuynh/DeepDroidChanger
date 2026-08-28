using System.IO;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyRuntimeResolver(string applicationBaseDirectory, string canonicalAdbPath)
{
    private static readonly string[] RequiredRuntimeFiles =
    [
        "scrcpy.exe",
        "scrcpy-server",
        "SDL3.dll",
        "avcodec-62.dll",
        "avformat-62.dll",
        "avutil-60.dll",
        "swresample-6.dll",
        "libusb-1.0.dll"
    ];

    public ScrcpyRuntimeInfo Resolve()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("View Device requires the official Windows scrcpy client.");
        if (!Environment.Is64BitOperatingSystem)
            throw new PlatformNotSupportedException("View Device requires 64-bit Windows.");

        string runtimeDirectory = ResolveRuntimeDirectory();
        string adbPath = Path.GetFullPath(canonicalAdbPath);
        if (!File.Exists(adbPath))
            throw new FileNotFoundException("The canonical DeepDroidChanger ADB executable was not found.", adbPath);

        foreach (string fileName in RequiredRuntimeFiles)
        {
            string requiredPath = Path.Combine(runtimeDirectory, fileName);
            if (!File.Exists(requiredPath))
                throw new FileNotFoundException($"The official scrcpy runtime is incomplete: {fileName} is missing.", requiredPath);
        }

        return new ScrcpyRuntimeInfo(
            runtimeDirectory,
            Path.Combine(runtimeDirectory, "scrcpy.exe"),
            Path.Combine(runtimeDirectory, "scrcpy-server"),
            adbPath);
    }

    private string ResolveRuntimeDirectory()
    {
        string relativePath = Path.Combine("Assets", "Tools", "scrcpy");
        string runtimePath = Path.GetFullPath(Path.Combine(applicationBaseDirectory, relativePath));
        if (Directory.Exists(runtimePath))
            return runtimePath;

        throw new DirectoryNotFoundException(
            $"The official scrcpy x64 runtime was not found at {runtimePath}.");
    }
}
