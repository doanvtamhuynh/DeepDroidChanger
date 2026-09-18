using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class AdbToolPathResolverTests
{
    [TestMethod]
    public void ResolveAdb_FromOutputPlatformTools()
    {
        using TestTempDirectory applicationBase = new();
        using TestTempDirectory projectDirectory = new();
        string path = CreateTool(applicationBase.Path, "adb.exe");

        AdbToolPathResolver resolver = new(applicationBase.Path, projectDirectory.Path);

        Assert.AreEqual(Path.GetFullPath(path), resolver.GetAdbPath());
    }

    [TestMethod]
    public void ResolveFastboot_FromOutputPlatformTools()
    {
        using TestTempDirectory applicationBase = new();
        using TestTempDirectory projectDirectory = new();
        string path = CreateTool(applicationBase.Path, "fastboot.exe");

        AdbToolPathResolver resolver = new(applicationBase.Path, projectDirectory.Path);

        Assert.AreEqual(Path.GetFullPath(path), resolver.GetFastbootPath());
    }

    [TestMethod]
    public void ResolveAdb_FromDevelopmentPlatformToolsFallback()
    {
        using TestTempDirectory applicationBase = new();
        using TestTempDirectory projectDirectory = new();
        string path = CreateTool(projectDirectory.Path, "adb.exe");

        AdbToolPathResolver resolver = new(applicationBase.Path, projectDirectory.Path);

        Assert.AreEqual(Path.GetFullPath(path), resolver.GetAdbPath());
    }

    [TestMethod]
    public void ResolveFastboot_FromDevelopmentPlatformToolsFallback()
    {
        using TestTempDirectory applicationBase = new();
        using TestTempDirectory projectDirectory = new();
        string path = CreateTool(projectDirectory.Path, "fastboot.exe");

        AdbToolPathResolver resolver = new(applicationBase.Path, projectDirectory.Path);

        Assert.AreEqual(Path.GetFullPath(path), resolver.GetFastbootPath());
    }

    [TestMethod]
    public void MissingAdb_ThrowsFileNotFound()
    {
        using TestTempDirectory applicationBase = new();
        using TestTempDirectory projectDirectory = new();
        AdbToolPathResolver resolver = new(applicationBase.Path, projectDirectory.Path);

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(
            resolver.GetAdbPath);

        StringAssert.Contains(exception.Message, "adb.exe");
        StringAssert.Contains(exception.Message, "platform-tools");
    }

    [TestMethod]
    public void MissingFastboot_ThrowsFileNotFound()
    {
        using TestTempDirectory applicationBase = new();
        using TestTempDirectory projectDirectory = new();
        AdbToolPathResolver resolver = new(applicationBase.Path, projectDirectory.Path);

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(
            resolver.GetFastbootPath);

        StringAssert.Contains(exception.Message, "fastboot.exe");
        StringAssert.Contains(exception.Message, "platform-tools");
    }

    [TestMethod]
    public void RootToolsAdb_IsNotAccidentallyPreferred()
    {
        using TestTempDirectory applicationBase = new();
        using TestTempDirectory projectDirectory = new();
        string rootAdb = Path.Combine(
            applicationBase.Path,
            "Assets",
            "Tools",
            "adb.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(rootAdb)!);
        File.WriteAllBytes(rootAdb, [0]);
        string canonicalAdb = CreateTool(projectDirectory.Path, "adb.exe");

        AdbToolPathResolver resolver = new(applicationBase.Path, projectDirectory.Path);

        Assert.AreEqual(Path.GetFullPath(canonicalAdb), resolver.GetAdbPath());
    }

    [TestMethod]
    public void SystemPathAdb_IsNotSilentlyUsed()
    {
        using TestTempDirectory applicationBase = new();
        using TestTempDirectory projectDirectory = new();
        AdbToolPathResolver resolver = new(applicationBase.Path, projectDirectory.Path);

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(
            resolver.GetAdbPath);

        Assert.AreNotEqual("adb.exe", exception.FileName);
        StringAssert.Contains(exception.Message, applicationBase.Path);
        StringAssert.Contains(exception.Message, projectDirectory.Path);
    }

    private static string CreateTool(string root, string name)
    {
        string path = Path.Combine(
            root,
            "Assets",
            "Tools",
            "platform-tools",
            name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
        return path;
    }
}
