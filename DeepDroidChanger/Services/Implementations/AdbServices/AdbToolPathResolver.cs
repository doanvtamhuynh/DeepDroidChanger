using System.IO;
using DeepDroidChanger.Constants;

namespace DeepDroidChanger.Services;

/// <summary>
/// Resolves the tools shipped with the application from one canonical location.
/// </summary>
public sealed class AdbToolPathResolver
{
    private readonly string _applicationBaseDirectory;
    private readonly string _projectDirectory;
    private readonly Lazy<string> _adbPath;
    private readonly Lazy<string> _fastbootPath;

    public AdbToolPathResolver()
        : this(AppContext.BaseDirectory, Environment.CurrentDirectory)
    {
    }

    internal AdbToolPathResolver(
        string applicationBaseDirectory,
        string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        _applicationBaseDirectory = Path.GetFullPath(applicationBaseDirectory);
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _adbPath = new(() => Resolve(AssetConstants.Tools.AdbExecutableName));
        _fastbootPath = new(() => Resolve(AssetConstants.Tools.FastbootExecutableName));
    }

    public string GetAdbPath()
    {
        return _adbPath.Value;
    }

    public string GetFastbootPath()
    {
        return _fastbootPath.Value;
    }

    private string Resolve(string executableName)
    {
        string outputPath = Path.Combine(
            _applicationBaseDirectory,
            AssetConstants.Tools.PlatformToolsRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar),
            executableName);
        string projectPath = Path.Combine(
            _projectDirectory,
            AssetConstants.Tools.PlatformToolsRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar),
            executableName);

        if (File.Exists(outputPath))
            return outputPath;

        if (File.Exists(projectPath))
            return projectPath;

        throw new FileNotFoundException(
            $"The bundled platform-tools executable '{executableName}' was not found. " +
            $"Attempted paths: '{outputPath}' and '{projectPath}'.",
            outputPath);
    }
}
