using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class PackageInstallServiceTests
{
    [TestMethod]
    public async Task InstallAsync_MissingOrUnsupportedFile_ReturnsValidationFailureWithoutAdb()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = CreateService(adb, Substitute.For<IXapkPackageService>());
        string unsupportedPath = CreateTempFile(".zip");
        try
        {
            InstallPackageResult missing = await service.InstallAsync(
                "SERIAL", unsupportedPath + ".missing", new InstallPackageOptions(false, false), CancellationToken.None);
            InstallPackageResult unsupported = await service.InstallAsync(
                "SERIAL", unsupportedPath, new InstallPackageOptions(false, false), CancellationToken.None);

            Assert.IsFalse(missing.Success);
            Assert.AreEqual("Log_InstallPackageFileMissing", missing.MessageResourceKey);
            Assert.IsFalse(unsupported.Success);
            Assert.AreEqual("Log_InstallPackageUnsupportedFile", unsupported.MessageResourceKey);
            await adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
        }
        finally
        {
            File.Delete(unsupportedPath);
        }
    }

    [DataRow("INSTALL_FAILED_ALREADY_EXISTS", "Log_InstallPackageAlreadyExists")]
    [DataRow("INSTALL_FAILED_INSUFFICIENT_STORAGE", "Log_InstallPackageInsufficientStorage")]
    [DataRow("INSTALL_FAILED_INVALID_APK", "Log_InstallPackageInvalidApk")]
    [DataRow("INSTALL_FAILED_NO_MATCHING_ABIS", "Log_InstallPackageNoMatchingAbis")]
    [DataRow("INSTALL_FAILED_MISSING_SHARED_LIBRARY: detail", "Log_InstallPackageMissingSharedLibrary")]
    [DataRow("INSTALL_FAILED_OTHER", "Log_InstallPackageAdbFailureCodeFormat")]
    [TestMethod]
    public async Task InstallAsync_KnownAdbFailure_MapsLocalizedCategory(string failureCode, string expectedKey)
    {
        string apkPath = CreateTempFile(".apk");
        try
        {
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult(1, $"Failure [{failureCode}]", string.Empty));
            var service = CreateService(adb, Substitute.For<IXapkPackageService>());

            InstallPackageResult result = await service.InstallAsync(
                "SERIAL", apkPath, new InstallPackageOptions(false, false), CancellationToken.None);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(expectedKey, result.MessageResourceKey);
        }
        finally
        {
            File.Delete(apkPath);
        }
    }

    [TestMethod]
    public async Task InstallAsync_ApkWithOptions_PassesGrantAndDowngradeFlags()
    {
        string apkPath = CreateTempFile(".apk");
        try
        {
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, "Success", string.Empty));
            var service = CreateService(adb, Substitute.For<IXapkPackageService>());

            InstallPackageResult result = await service.InstallAsync(
                "SERIAL",
                apkPath,
                new InstallPackageOptions(grantPermissions: true, allowDowngrade: true),
                CancellationToken.None);

            Assert.IsTrue(result.Success);
            await adb.Received(1).RunAdbAsync(
                "SERIAL",
                Arg.Is<string>(arguments =>
                    arguments.Contains("-g -d", StringComparison.Ordinal)
                    && arguments.Contains(apkPath, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(apkPath);
        }
    }

    [TestMethod]
    public async Task InstallAsync_AdbFailure_MapsFailureCode()
    {
        string apkPath = CreateTempFile(".apk");
        try
        {
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult(1, "Failure [INSTALL_FAILED_VERSION_DOWNGRADE]", string.Empty));
            var service = CreateService(adb, Substitute.For<IXapkPackageService>());

            InstallPackageResult result = await service.InstallAsync(
                "SERIAL", apkPath, new InstallPackageOptions(false, false), CancellationToken.None);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("INSTALL_FAILED_VERSION_DOWNGRADE", result.FailureCode);
        }
        finally
        {
            File.Delete(apkPath);
        }
    }

    [TestMethod]
    public async Task InstallAsync_XapkExtractionFailure_AlwaysDeletesTemporaryDirectory()
    {
        string xapkPath = CreateTempFile(".xapk");
        string? extractionDirectory = null;
        try
        {
            IXapkPackageService xapk = Substitute.For<IXapkPackageService>();
            xapk.ExtractAsync(xapkPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<Task<XapkPackageInfo>>(callInfo =>
                {
                    extractionDirectory = callInfo.ArgAt<string>(1);
                    Directory.CreateDirectory(extractionDirectory);
                    File.WriteAllText(Path.Combine(extractionDirectory, "partial.tmp"), "partial");
                    return Task.FromException<XapkPackageInfo>(new InvalidDataException("invalid archive"));
                });
            var service = CreateService(Substitute.For<IAdbCommandService>(), xapk);

            InstallPackageResult result = await service.InstallAsync(
                "SERIAL", xapkPath, new InstallPackageOptions(false, false), CancellationToken.None);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(extractionDirectory);
            Assert.IsFalse(Directory.Exists(extractionDirectory));
        }
        finally
        {
            File.Delete(xapkPath);
            if (extractionDirectory != null && Directory.Exists(extractionDirectory))
                Directory.Delete(extractionDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task InstallAsync_XapkWithSplitsAndObb_InstallsAndPushesAllPayloads()
    {
        string xapkPath = CreateTempFile(".xapk");
        try
        {
            IXapkPackageService xapk = Substitute.For<IXapkPackageService>();
            xapk.ExtractAsync(xapkPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    string directory = call.ArgAt<string>(1);
                    string obbPath = Path.Combine(directory, "main.1.example.obb");
                    return new XapkPackageInfo(
                        "com.example",
                        [Path.Combine(directory, "base.apk"), Path.Combine(directory, "config.apk")],
                        [new ObbFileInfo(obbPath, Path.GetFileName(obbPath))]);
                });
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, "Success", string.Empty));
            adb.RunAdbShellAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, string.Empty, string.Empty));
            var service = CreateService(adb, xapk);

            InstallPackageResult result = await service.InstallAsync(
                "SERIAL", xapkPath, new InstallPackageOptions(true, true), CancellationToken.None);

            Assert.IsTrue(result.Success);
            await adb.Received(1).RunAdbShellAsync(
                "SERIAL", Arg.Is<string>(value => value.Contains("com.example", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
            await adb.Received(2).RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(xapkPath);
        }
    }

    [TestMethod]
    public async Task InstallAsync_XapkObbDirectoryFailure_ReturnsObbFailure()
    {
        string xapkPath = CreateTempFile(".xapk");
        try
        {
            IXapkPackageService xapk = Substitute.For<IXapkPackageService>();
            xapk.ExtractAsync(xapkPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    string directory = call.ArgAt<string>(1);
                    return new XapkPackageInfo(
                        "com.example",
                        [Path.Combine(directory, "base.apk")],
                        [new ObbFileInfo(Path.Combine(directory, "main.obb"), "main.obb")]);
                });
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, "Success", string.Empty));
            adb.RunAdbShellAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult(1, string.Empty, "mkdir failed"));
            var service = CreateService(adb, xapk);

            InstallPackageResult result = await service.InstallAsync(
                "SERIAL", xapkPath, new InstallPackageOptions(false, false), CancellationToken.None);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Log_InstallPackageObbPushFailed", result.MessageResourceKey);
        }
        finally
        {
            File.Delete(xapkPath);
        }
    }

    [TestMethod]
    public async Task InstallAsync_PreCanceled_DoesNotInvokeAdbOrExtraction()
    {
        string apkPath = CreateTempFile(".apk");
        try
        {
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            IXapkPackageService xapk = Substitute.For<IXapkPackageService>();
            var service = CreateService(adb, xapk);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                service.InstallAsync(
                    "SERIAL", apkPath, new InstallPackageOptions(false, false), cancellation.Token));

            await adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
            await xapk.DidNotReceiveWithAnyArgs().ExtractAsync(default!, default!, default);
        }
        finally
        {
            File.Delete(apkPath);
        }
    }

    private static PackageInstallService CreateService(IAdbCommandService adb, IXapkPackageService xapk)
    {
        return new PackageInstallService(adb, xapk, NullLogger<PackageInstallService>.Instance);
    }

    private static string CreateTempFile(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, []);
        return path;
    }
}
