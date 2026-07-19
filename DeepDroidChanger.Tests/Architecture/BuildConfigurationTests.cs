using DeepDroidChanger.Helpers;
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
    public void RequiredRuntimeTools_AreCopiedToTestOutput()
    {
        string[] requiredRelativePaths =
        [
            "Assets/Tools/platform-tools/adb.exe",
            "Assets/Tools/platform-tools/fastboot.exe",
            "Assets/Tools/viewscreen/scrcpy.exe",
            "Assets/Tools/viewscreen/scrcpy-server",
        ];
        string[] missing = requiredRelativePaths
            .Where(relativePath => !File.Exists(Path.Combine(
                AppContext.BaseDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        Assert.IsEmpty(missing, $"Missing runtime output assets: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void DataAssets_AreEmbeddedAndNotCopiedToApplicationOutput()
    {
        string[] expectedResourceNames =
        [
            "DeepDroidChanger.Assets.Data.bip0039.txt",
            "DeepDroidChanger.Assets.Data.carriers.json",
            "DeepDroidChanger.Assets.Data.imei_tacs.json",
            "DeepDroidChanger.Assets.Data.mac_vendors.json",
            "DeepDroidChanger.Assets.Data.names.txt",
            "DeepDroidChanger.Assets.Data.timezones.json",
        ];
        string[] actualResourceNames = typeof(AssetDataReader).Assembly.GetManifestResourceNames();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Unable to resolve the active build configuration.");
        string outputDataDirectory = Path.Combine(
            GetSolutionRoot(),
            "DeepDroidChanger",
            "bin",
            configuration,
            "net10.0-windows",
            "Assets",
            "Data");

        Assert.IsTrue(expectedResourceNames.All(actualResourceNames.Contains));
        Assert.IsFalse(Directory.Exists(outputDataDirectory));
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
