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
    public void Resolve_CompleteRuntime_ReturnsScrcpyNetPaths()
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

    private static string CreateRuntime(string root)
    {
        string runtimeDirectory = Path.Combine(root, "Assets", "Tools");
        Directory.CreateDirectory(runtimeDirectory);
        foreach (string fileName in RequiredRuntimeFiles)
            File.WriteAllBytes(Path.Combine(runtimeDirectory, fileName), [0]);
        return runtimeDirectory;
    }
}
