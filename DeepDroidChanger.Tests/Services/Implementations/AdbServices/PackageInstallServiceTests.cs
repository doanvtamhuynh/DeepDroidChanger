using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class PackageInstallServiceTests
{
    [TestMethod]
    public async Task InstallAsync_Apk_UsesReplaceOnlyWithoutOptionalFlags()
    {
        string testRoot = CreateTestRoot();
        try
        {
            string apkPath = Path.Combine(testRoot, "sample app.apk");
            File.WriteAllText(apkPath, "apk");
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, "Success", string.Empty));
            var service = new PackageInstallService(
                adb,
                Substitute.For<IXapkPackageService>(),
                NullLogger<PackageInstallService>.Instance);

            InstallPackageResult result = await service.InstallAsync(
                "SERIAL",
                apkPath,
                new InstallPackageOptions(grantPermissions: false, allowDowngrade: false),
                CancellationToken.None);

            Assert.IsTrue(result.Success);
            await adb.Received(1).RunAdbAsync(
                "SERIAL",
                Arg.Is<string>(arguments => arguments == $"install -r \"{apkPath}\""),
                CancellationToken.None);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task InstallAsync_ApkFailure_MapsAdbFailureCodeFromStandardError()
    {
        string testRoot = CreateTestRoot();
        try
        {
            string apkPath = Path.Combine(testRoot, "sample.apk");
            File.WriteAllText(apkPath, "apk");
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult(
                    1,
                    string.Empty,
                    "Failure [INSTALL_FAILED_VERSION_DOWNGRADE: older version]"));
            var service = new PackageInstallService(
                adb,
                Substitute.For<IXapkPackageService>(),
                NullLogger<PackageInstallService>.Instance);

            InstallPackageResult result = await service.InstallAsync(
                "SERIAL",
                apkPath,
                new InstallPackageOptions(grantPermissions: false, allowDowngrade: false),
                CancellationToken.None);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("INSTALL_FAILED_VERSION_DOWNGRADE: older version", result.FailureCode);
            Assert.AreEqual("Log_InstallPackageVersionDowngrade", result.MessageResourceKey);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string CreateTestRoot()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "DeepDroidChanger.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
