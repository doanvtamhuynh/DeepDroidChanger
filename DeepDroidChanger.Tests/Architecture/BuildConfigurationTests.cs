using System.Xml.Linq;

namespace DeepDroidChanger.Tests.Architecture;

[TestClass]
public sealed class BuildConfigurationTests
{
    [TestMethod]
    public void Manifest_ConfiguresPerMonitorV2AndProjectReferencesIt()
    {
        string projectRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
        XDocument project = XDocument.Load(Path.Combine(projectRoot, "DeepDroidChanger.csproj"));
        XDocument manifest = XDocument.Load(Path.Combine(projectRoot, "app.manifest"));

        string? manifestReference = project.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "ApplicationManifest")
            ?.Value;
        string? dpiAwareness = manifest.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "dpiAwareness")
            ?.Value;

        Assert.AreEqual("app.manifest", manifestReference);
        Assert.AreEqual("PerMonitorV2", dpiAwareness);
    }

    [TestMethod]
    public void RequiredRuntimeAssets_AreCopiedToTestOutput()
    {
        string[] requiredRelativePaths =
        [
            "Assets/Data/carriers.json",
            "Assets/Data/timezones.json",
            "Assets/Tools/platform-tools/adb.exe",
            "Assets/Tools/platform-tools/fastboot.exe",
            "Assets/Tools/platform-tools/scrcpy.exe",
            "Assets/Tools/platform-tools/scrcpy-server",
        ];
        string[] missing = requiredRelativePaths
            .Where(relativePath => !File.Exists(Path.Combine(
                AppContext.BaseDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        Assert.IsEmpty(missing, $"Missing runtime output assets: {string.Join(", ", missing)}");
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
