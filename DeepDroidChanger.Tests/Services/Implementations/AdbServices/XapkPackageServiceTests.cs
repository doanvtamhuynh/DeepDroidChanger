using System.IO.Compression;
using System.Text;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class XapkPackageServiceTests
{
    [TestMethod]
    public async Task ExtractAsync_ValidPackage_ReturnsBaseApkFirst()
    {
        string testRoot = CreateTestRoot();
        try
        {
            string archivePath = Path.Combine(testRoot, "package.xapk");
            CreateArchive(
                archivePath,
                ("manifest.json", "{\"package_name\":\"com.example.app\"}"),
                ("split_config.en.apk", "split"),
                ("base.apk", "base"),
                ("Android/obb/main.1.com.example.app.obb", "obb"));
            string output = Path.Combine(testRoot, "output");
            var service = new XapkPackageService(NullLogger<XapkPackageService>.Instance);

            var result = await service.ExtractAsync(archivePath, output, CancellationToken.None);

            Assert.AreEqual("com.example.app", result.PackageName);
            Assert.AreEqual("base.apk", Path.GetFileName(result.ApkFilePaths[0]));
            Assert.HasCount(2, result.ApkFilePaths);
            Assert.HasCount(1, result.ObbFiles);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExtractAsync_PathTraversalEntry_IsRejected()
    {
        string testRoot = CreateTestRoot();
        try
        {
            string archivePath = Path.Combine(testRoot, "malicious.xapk");
            CreateArchive(
                archivePath,
                ("manifest.json", "{\"package_name\":\"com.example.app\"}"),
                ("base.apk", "base"),
                ("../escaped.apk", "malicious"));
            string output = Path.Combine(testRoot, "output");
            string escapedPath = Path.Combine(testRoot, "escaped.apk");
            var service = new XapkPackageService(NullLogger<XapkPackageService>.Instance);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                service.ExtractAsync(archivePath, output, CancellationToken.None));

            Assert.IsFalse(File.Exists(escapedPath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExtractAsync_PreCanceled_DoesNotCreateOutputDirectory()
    {
        string testRoot = CreateTestRoot();
        try
        {
            string archivePath = Path.Combine(testRoot, "package.xapk");
            CreateArchive(archivePath, ("manifest.json", "{}"));
            string output = Path.Combine(testRoot, "output");
            var service = new XapkPackageService(NullLogger<XapkPackageService>.Instance);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                service.ExtractAsync(archivePath, output, cancellation.Token));

            Assert.IsFalse(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string CreateTestRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "DeepDroidChanger.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateArchive(string path, params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using Stream stream = entry.Open();
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes);
        }
    }
}
