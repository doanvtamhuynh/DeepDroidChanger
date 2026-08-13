using System.IO;
using DeepDroidChanger.Constants;

namespace DeepDroidChanger.Services;

/// <summary>
/// Resolves the tools shipped with the application from one canonical location.
/// </summary>
public sealed class AdbToolPathResolver
{
    private readonly Lazy<string> _adbPath = new(() => Resolve(
        AssetConstants.Tools.PlatformToolsDirectoryName,
        AssetConstants.Tools.AdbExecutableName));
    private readonly Lazy<string> _fastbootPath = new(() => Resolve(
        AssetConstants.Tools.PlatformToolsDirectoryName,
        AssetConstants.Tools.FastbootExecutableName));
    private readonly Lazy<string> _scrcpyPath = new(() => Resolve(
        AssetConstants.Tools.ViewScreenDirectoryName,
        AssetConstants.Tools.ScrcpyExecutableName));

    public string GetAdbPath()
    {
        return _adbPath.Value;
    }

    public string GetFastbootPath()
    {
        return _fastbootPath.Value;
    }

    public string GetScrcpyPath()
    {
        return _scrcpyPath.Value;
    }

    private static string Resolve(string directoryName, string executableName)
    {
        var outputPath = Path.Combine(
            AppContext.BaseDirectory,
            AssetConstants.Tools.RootRelativePath,
            directoryName,
            executableName);
        var projectPath = Path.Combine(
            Environment.CurrentDirectory,
            AssetConstants.Tools.RootRelativePath,
            directoryName,
            executableName);

        if (File.Exists(outputPath))
            return outputPath;

        if (File.Exists(projectPath))
            return projectPath;

        return executableName;
    }
}
