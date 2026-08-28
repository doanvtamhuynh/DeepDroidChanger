using System.Diagnostics;
using DeepDroidChanger.Tests.Helpers;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ScrcpyRuntimeTests
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

    [TestMethod]
    public void Resolve_CompleteX64Runtime_ReturnsFlatRuntimePaths()
    {
        using TestTempDirectory temporary = new();
        string runtimeDirectory = CreateRuntime(temporary.Path);
        string adbPath = CreateFile(temporary.Path, "canonical-adb.exe");
        var resolver = new ScrcpyRuntimeResolver(temporary.Path, adbPath);

        ScrcpyRuntimeInfo runtime = resolver.Resolve();

        Assert.AreEqual(Path.GetFullPath(runtimeDirectory), runtime.RuntimeDirectory);
        Assert.AreEqual(Path.Combine(runtimeDirectory, "scrcpy.exe"), runtime.ExecutablePath);
        Assert.AreEqual(Path.Combine(runtimeDirectory, "scrcpy-server"), runtime.ServerPath);
        Assert.AreEqual(Path.GetFullPath(adbPath), runtime.CanonicalAdbPath);
    }

    [TestMethod]
    [DataRow("scrcpy.exe")]
    [DataRow("scrcpy-server")]
    [DataRow("SDL3.dll")]
    [DataRow("avcodec-62.dll")]
    [DataRow("avformat-62.dll")]
    [DataRow("avutil-60.dll")]
    [DataRow("swresample-6.dll")]
    [DataRow("libusb-1.0.dll")]
    public void Resolve_MissingRequiredRuntimeFile_ThrowsUsefulFileNotFoundException(string missingFile)
    {
        using TestTempDirectory temporary = new();
        string runtimeDirectory = CreateRuntime(temporary.Path, missingFile);
        string adbPath = CreateFile(temporary.Path, "canonical-adb.exe");
        var resolver = new ScrcpyRuntimeResolver(temporary.Path, adbPath);

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(() => resolver.Resolve());

        StringAssert.Contains(exception.Message, missingFile);
        Assert.AreEqual(Path.Combine(runtimeDirectory, missingFile), exception.FileName);
    }

    [TestMethod]
    public void Resolve_MissingCanonicalAdb_ThrowsUsefulFileNotFoundException()
    {
        using TestTempDirectory temporary = new();
        _ = CreateRuntime(temporary.Path);
        string missingAdbPath = Path.Combine(temporary.Path, "missing-adb.exe");
        var resolver = new ScrcpyRuntimeResolver(temporary.Path, missingAdbPath);

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(() => resolver.Resolve());

        StringAssert.Contains(exception.Message, "canonical DeepDroidChanger ADB");
        Assert.AreEqual(Path.GetFullPath(missingAdbPath), exception.FileName);
    }

    [TestMethod]
    [DoNotParallelize]
    public void Resolve_RuntimeOnlyUnderCurrentDirectory_DoesNotFallBack()
    {
        using TestTempDirectory applicationBase = new();
        using TestTempDirectory currentDirectory = new();
        _ = CreateRuntime(currentDirectory.Path);
        string adbPath = CreateFile(currentDirectory.Path, "canonical-adb.exe");
        string originalCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = currentDirectory.Path;
            var resolver = new ScrcpyRuntimeResolver(applicationBase.Path, adbPath);

            DirectoryNotFoundException exception =
                Assert.ThrowsExactly<DirectoryNotFoundException>(() => resolver.Resolve());

            StringAssert.Contains(
                exception.Message,
                Path.Combine(applicationBase.Path, "Assets", "Tools", "scrcpy"));
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    [TestMethod]
    public void CreateStartInfo_UsesCanonicalRuntimeAndBalancedArguments()
    {
        const string serial = "SERIAL-123";
        const string windowTitle = "DeepDroidChanger.ViewDevice.test";
        string runtimeDirectory = Path.GetFullPath(Path.Combine("runtime", "scrcpy"));
        string executablePath = Path.Combine(runtimeDirectory, "scrcpy.exe");
        string serverPath = Path.Combine(runtimeDirectory, "scrcpy-server");
        string adbPath = Path.GetFullPath(Path.Combine("tools", "platform-tools", "adb.exe"));
        var runtime = new ScrcpyRuntimeInfo(runtimeDirectory, executablePath, serverPath, adbPath);

        ProcessStartInfo startInfo = ScrcpyProcessSession.CreateStartInfo(
            new ViewDeviceLaunchOptions(serial),
            runtime,
            windowTitle);
        string[] arguments = startInfo.ArgumentList.ToArray();

        Assert.AreEqual(executablePath, startInfo.FileName);
        Assert.AreEqual(runtimeDirectory, startInfo.WorkingDirectory);
        Assert.AreEqual(adbPath, startInfo.Environment["ADB"]);
        Assert.AreEqual(serverPath, startInfo.Environment["SCRCPY_SERVER_PATH"]);
        CollectionAssert.Contains(arguments, "--serial");
        CollectionAssert.Contains(arguments, serial);
        CollectionAssert.Contains(arguments, "--window-title");
        CollectionAssert.Contains(arguments, windowTitle);
        CollectionAssert.Contains(arguments, "--window-x=-32000");
        CollectionAssert.Contains(arguments, "--window-y=-32000");
        CollectionAssert.Contains(arguments, "--video-codec=h264");
        CollectionAssert.Contains(arguments, "--max-size=1280");
        CollectionAssert.Contains(arguments, "--max-fps=30");
        CollectionAssert.Contains(arguments, "--video-bit-rate=4M");
        CollectionAssert.DoesNotContain(arguments, "--no-clipboard-autosync");
        CollectionAssert.DoesNotContain(arguments, "--no-audio");
        Assert.IsFalse(arguments.Any(argument => argument.StartsWith("--keyboard=", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("Texture: 1080x2400", 1080, 2400)]
    [DataRow("Texture: 2400x1080", 2400, 1080)]
    [DataRow("INFO: Texture: 720x1280", 720, 1280)]
    [DataRow("[server] INFO: Texture: 1280x720", 1280, 720)]
    public void TryParseTextureSize_ValidDiagnostic_ReturnsDimensions(
        string diagnostic,
        int expectedWidth,
        int expectedHeight)
    {
        bool parsed = ScrcpyProcessSession.TryParseTextureSize(diagnostic, out int width, out int height);

        Assert.IsTrue(parsed);
        Assert.AreEqual(expectedWidth, width);
        Assert.AreEqual(expectedHeight, height);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("INFO: video started")]
    [DataRow("Texture: invalid")]
    [DataRow("Texture: 0x2400")]
    [DataRow("Texture: 1080x0")]
    public void TryParseTextureSize_InvalidDiagnostic_ReturnsFalse(string? diagnostic)
    {
        bool parsed = ScrcpyProcessSession.TryParseTextureSize(diagnostic, out int width, out int height);

        Assert.IsFalse(parsed);
        Assert.AreEqual(0, width);
        Assert.AreEqual(0, height);
    }

    private static string CreateRuntime(string root, string? excludedFile = null)
    {
        string runtimeDirectory = Path.Combine(root, "Assets", "Tools", "scrcpy");
        Directory.CreateDirectory(runtimeDirectory);
        foreach (string file in RequiredRuntimeFiles)
        {
            if (!string.Equals(file, excludedFile, StringComparison.OrdinalIgnoreCase))
                File.WriteAllBytes(Path.Combine(runtimeDirectory, file), [0]);
        }

        return runtimeDirectory;
    }

    private static string CreateFile(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, [0]);
        return path;
    }
}
