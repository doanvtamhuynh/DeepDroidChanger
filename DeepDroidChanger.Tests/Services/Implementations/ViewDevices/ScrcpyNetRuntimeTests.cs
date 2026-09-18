using DeepDroidChanger.Tests.Helpers;
using DeepDroidChanger.ViewDevices.Runtime;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ScrcpyNetRuntimeTests
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

    [TestMethod]
    public void Resolve_ReturnsViewDeviceDirectory()
    {
        using TestTempDirectory temporary = new();
        string runtimeDirectory = CreateRuntime(temporary.Path);
        string adbPath = Path.Combine(temporary.Path, "adb.exe");
        File.WriteAllBytes(adbPath, [0]);

        ScrcpyNetRuntimeInfo runtime = new ScrcpyNetRuntimeResolver(
            temporary.Path,
            adbPath).Resolve();

        Assert.AreEqual(Path.GetFullPath(runtimeDirectory), runtime.RuntimeDirectory);
        Assert.AreEqual(
            Path.Combine(runtimeDirectory, "scrcpy-server.jar"),
            runtime.ServerPath);
        Assert.AreEqual(Path.GetFullPath(adbPath), runtime.CanonicalAdbPath);
    }

    [TestMethod]
    public void Resolve_ReturnsJarInsideViewDeviceDirectory()
    {
        using TestTempDirectory temporary = new();
        string runtimeDirectory = CreateRuntime(temporary.Path);
        string adbPath = Path.Combine(temporary.Path, "adb.exe");
        File.WriteAllBytes(adbPath, [0]);

        ScrcpyNetRuntimeInfo runtime = new ScrcpyNetRuntimeResolver(
            temporary.Path,
            adbPath).Resolve();

        Assert.AreEqual(
            Path.Combine(temporary.Path, "Assets", "Tools", "viewdevice"),
            runtime.RuntimeDirectory);
        Assert.AreEqual(
            Path.Combine(runtime.RuntimeDirectory, "scrcpy-server.jar"),
            runtime.ServerPath);
    }

    [TestMethod]
    public void Resolve_PreservesCanonicalPlatformToolsAdb()
    {
        using TestTempDirectory temporary = new();
        _ = CreateRuntime(temporary.Path);
        string adbPath = Path.Combine(
            temporary.Path,
            "Assets",
            "Tools",
            "platform-tools",
            "adb.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(adbPath)!);
        File.WriteAllBytes(adbPath, [0]);

        ScrcpyNetRuntimeInfo runtime = new ScrcpyNetRuntimeResolver(
            temporary.Path,
            adbPath).Resolve();

        Assert.AreEqual(Path.GetFullPath(adbPath), runtime.CanonicalAdbPath);
    }

    [TestMethod]
    public void MissingViewDeviceDirectory_Throws()
    {
        using TestTempDirectory temporary = new();
        string adbPath = Path.Combine(temporary.Path, "adb.exe");
        File.WriteAllBytes(adbPath, [0]);

        DirectoryNotFoundException exception = Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => new ScrcpyNetRuntimeResolver(temporary.Path, adbPath).Resolve());

        StringAssert.Contains(
            exception.Message,
            Path.Combine(temporary.Path, "Assets", "Tools", "viewdevice"));
    }

    [TestMethod]
    public void MissingScrcpyServer_Throws()
    {
        AssertMissingRuntimeFile("scrcpy-server.jar");
    }

    [TestMethod]
    public void MissingAvcodec_Throws()
    {
        AssertMissingRuntimeFile("avcodec-59.dll");
    }

    [TestMethod]
    public void MissingAvformat_Throws()
    {
        AssertMissingRuntimeFile("avformat-59.dll");
    }

    [TestMethod]
    public void MissingAvutil_Throws()
    {
        AssertMissingRuntimeFile("avutil-57.dll");
    }

    [TestMethod]
    public void MissingSwresample_Throws()
    {
        AssertMissingRuntimeFile("swresample-4.dll");
    }

    [TestMethod]
    public void MissingSwscale_Throws()
    {
        AssertMissingRuntimeFile("swscale-6.dll");
    }

    [TestMethod]
    [DataRow("scrcpy-server.jar")]
    [DataRow("avcodec-59.dll")]
    [DataRow("avformat-59.dll")]
    [DataRow("avutil-57.dll")]
    [DataRow("swresample-4.dll")]
    [DataRow("swscale-6.dll")]
    public void MissingRequiredViewDeviceFile_Throws(string missingFile)
    {
        using TestTempDirectory temporary = new();
        string runtimeDirectory = CreateRuntime(temporary.Path, missingFile);
        string adbPath = Path.Combine(temporary.Path, "adb.exe");
        File.WriteAllBytes(adbPath, [0]);

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(
            () => new ScrcpyNetRuntimeResolver(temporary.Path, adbPath).Resolve());

        StringAssert.Contains(exception.Message, missingFile);
        Assert.AreEqual(Path.Combine(runtimeDirectory, missingFile), exception.FileName);
    }

    [TestMethod]
    public void MissingCanonicalAdb_Throws()
    {
        using TestTempDirectory temporary = new();
        _ = CreateRuntime(temporary.Path);
        string missingAdbPath = Path.Combine(temporary.Path, "missing-adb.exe");

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(
            () => new ScrcpyNetRuntimeResolver(temporary.Path, missingAdbPath).Resolve());

        StringAssert.Contains(exception.Message, "canonical DeepDroidChanger ADB");
        Assert.AreEqual(Path.GetFullPath(missingAdbPath), exception.FileName);
    }

    [TestMethod]
    [DataRow("4M", 4_000_000L)]
    [DataRow("2m", 2_000_000L)]
    [DataRow("800K", 800_000L)]
    [DataRow("123456", 123456L)]
    public void ParseBitrate_AcceptsScrcpyBitrateSuffixes(string value, long expected)
    {
        Assert.AreEqual(expected, ScrcpyNetClientFactory.ParseBitrate(value));
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("fast")]
    [DataRow("4G")]
    public void ParseBitrate_InvalidValueThrows(string value)
    {
        Assert.ThrowsExactly<FormatException>(() => ScrcpyNetClientFactory.ParseBitrate(value));
    }

    [TestMethod]
    public void ParseBitrate_EmptyValueThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ScrcpyNetClientFactory.ParseBitrate(string.Empty));
    }

    private static string CreateRuntime(string root, string? excludedFile = null)
    {
        string runtimeDirectory = Path.Combine(root, "Assets", "Tools", "viewdevice");
        Directory.CreateDirectory(runtimeDirectory);
        foreach (string fileName in RequiredRuntimeFiles)
        {
            if (!string.Equals(fileName, excludedFile, StringComparison.OrdinalIgnoreCase))
                File.WriteAllBytes(Path.Combine(runtimeDirectory, fileName), [0]);
        }

        return runtimeDirectory;
    }

    private static void AssertMissingRuntimeFile(string missingFile)
    {
        using TestTempDirectory temporary = new();
        string runtimeDirectory = CreateRuntime(temporary.Path, missingFile);
        string adbPath = Path.Combine(temporary.Path, "adb.exe");
        File.WriteAllBytes(adbPath, [0]);

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(
            () => new ScrcpyNetRuntimeResolver(temporary.Path, adbPath).Resolve());

        StringAssert.Contains(exception.Message, missingFile);
        Assert.AreEqual(Path.Combine(runtimeDirectory, missingFile), exception.FileName);
    }
}
