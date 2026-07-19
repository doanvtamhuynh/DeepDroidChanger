using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class DeviceDataCleanupServiceTests
{
    [TestMethod]
    public async Task CleanAsync_AdvancedWithoutPackageOrAccountCleanup_SendsOneConsolidatedScript()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanAsync(
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false,
                ClearGoogleAccounts = false
            },
            CancellationToken.None);

        await packages.DidNotReceiveWithAnyArgs().GetInstalledPackagesAsync(default!, default);
        string script = GetOnlyCleanupScript(adb);
        Assert.DoesNotContain("/data/system_ce", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/data/system_de", script, StringComparison.Ordinal);
        Assert.Contains("/data/misc_ce", script, StringComparison.Ordinal);
        Assert.Contains("/data/misc_de", script, StringComparison.Ordinal);
        AssertCleanupFinalization(script);
        AssertNoUnsafeResidualRemove(script);
        await adb.DidNotReceiveWithAnyArgs().RunAdbShellAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task CleanAsync_WithRealPackageService_UsesOneListCommandAndOneScriptCommand()
    {
        IAdbCommandService adb = CreateSuccessfulAdb();
        adb.RunAdbShellAsync("SERIAL", "pm list packages", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(
                0,
                "package:com.example.one\npackage:com.example.two\n",
                string.Empty));
        var service = CreateService(new DevicePackageService(adb), adb);

        await service.CleanAsync(
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = true,
                ClearGoogleAccounts = false
            },
            CancellationToken.None);

        await adb.Received(1).RunAdbShellAsync(
            "SERIAL",
            "pm list packages",
            Arg.Any<CancellationToken>());
        string script = GetOnlyCleanupScript(adb);
        Assert.Contains(
            "for package in com.example.one com.example.two; do",
            script,
            StringComparison.Ordinal);
        int cleanupAdbCalls = adb.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name is nameof(IAdbCommandService.RunAdbShellAsync)
                or nameof(IAdbCommandService.RunAdbShellScriptAsync));
        Assert.AreEqual(2, cleanupAdbCalls);
    }

    [TestMethod]
    public async Task CleanAsync_DefaultMode_BatchesPackagesAccountAndResidualCleanupWithoutRmRf()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(["com.android.shell", "com.example.app", "com.google.android.gms"]);
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanAsync(
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = true,
                ClearAllPackages = true,
                UseRmRfForPackageCleanup = true
            },
            CancellationToken.None);

        string script = GetOnlyCleanupScript(adb);
        Assert.Contains(
            "for package in com.example.app com.google.android.gms; do",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("for package in com.android.shell", script, StringComparison.Ordinal);
        Assert.Contains("pm clear \"$package\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf", script, StringComparison.Ordinal);
        Assert.Contains("/data/system_ce /data/system_de", script, StringComparison.Ordinal);
        Assert.Contains("/data/system/*.db*", script, StringComparison.Ordinal);
        Assert.Contains(DeviceDataCleanupService.SsaidFilePattern, script, StringComparison.Ordinal);
        Assert.Contains("case \"$target\" in /data/system/package*)", script, StringComparison.Ordinal);
        AssertCleanupFinalization(script);
        await packages.Received(1).GetInstalledPackagesAsync(
            "SERIAL",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task CleanPreservingSsaidAsync_DefaultMode_ExcludesOnlySsaidFilePattern()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns([]);
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanPreservingSsaidAsync(
            "SERIAL",
            new DeviceChangeOptions { UseDefaultMode = true },
            CancellationToken.None);

        string script = GetOnlyCleanupScript(adb);
        Assert.DoesNotContain(DeviceDataCleanupService.SsaidFilePattern, script, StringComparison.Ordinal);
        foreach (string pattern in DeviceDataCleanupService.ResidualFilePatterns
                     .Where(pattern => pattern != DeviceDataCleanupService.SsaidFilePattern))
        {
            Assert.Contains(pattern, script, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public async Task CleanAsync_AdvancedAccountCleanup_BatchesGooglePackagesAndAccountPatterns()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(["com.google.android.gms", "com.google.android.gsf.login", "com.android.vending"]);
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanAsync(
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false,
                ClearGoogleAccounts = true
            },
            CancellationToken.None);

        string script = GetOnlyCleanupScript(adb);
        Assert.Contains(
            "for package in com.android.vending com.google.android.gms com.google.android.gsf.login; do",
            script,
            StringComparison.Ordinal);
        Assert.Contains("/data/system_ce /data/system_de", script, StringComparison.Ordinal);
        Assert.Contains(
            string.Join(' ', DeviceDataCleanupService.AccountFilePatterns),
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/data/system/*.db*", script, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CleanAsync_SelectedAndGooglePackages_EmbedsOnlyInstalledTargets()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns([
                "com.example.installed",
                "com.google.android.gms",
                "com.android.vending",
                "com.google.android.youtube"
            ]);
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanAsync(
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false,
                ClearSelectedPackages = true,
                SelectedPackages = ["com.example.installed", "com.example.missing"],
                ClearGooglePackages = true,
                ClearGoogleAccounts = false
            },
            CancellationToken.None);

        string script = GetOnlyCleanupScript(adb);
        Assert.Contains("com.example.installed", script, StringComparison.Ordinal);
        Assert.Contains("com.google.android.gms", script, StringComparison.Ordinal);
        Assert.Contains("com.android.vending", script, StringComparison.Ordinal);
        Assert.Contains("com.google.android.youtube", script, StringComparison.Ordinal);
        Assert.DoesNotContain("com.example.missing", script, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CleanAsync_RmRfOption_BatchesAllPackagesAndEightPathsInSameScript()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns([
                "com.android.shell",
                "com.example.selected",
                "com.google.android.gms",
                "com.google.android.youtube",
                "com.android.vending"
            ]);
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanAsync(
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                UseRmRfForPackageCleanup = true,
                ClearAllPackages = false,
                ClearSelectedPackages = true,
                SelectedPackages = ["com.example.selected"],
                ClearGooglePackages = true,
                ClearGoogleAccounts = true
            },
            CancellationToken.None);

        string script = GetOnlyCleanupScript(adb);
        Assert.Contains(
            "for package in com.android.vending com.example.selected com.google.android.gms com.google.android.youtube; do",
            script,
            StringComparison.Ordinal);
        Assert.Contains("rm -rf", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pm clear", script, StringComparison.Ordinal);
        Assert.DoesNotContain("for package in com.android.shell", script, StringComparison.Ordinal);
        foreach (string pathTemplate in AdbCleanupCommandBuilder.RmRfPackagePathTemplates)
        {
            Assert.Contains(
                pathTemplate.Replace("{package}", "$package", StringComparison.Ordinal),
                script,
                StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public async Task CleanAsync_ScriptFailure_ThrowsWithoutIssuingFallbackCommands()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellScriptAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "permission denied"));
        var service = CreateService(packages, adb);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.CleanAsync(
                "SERIAL",
                new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ClearAllPackages = false,
                    ClearGoogleAccounts = false
                },
                CancellationToken.None));

        _ = GetOnlyCleanupScript(adb);
        await adb.DidNotReceiveWithAnyArgs().RunAdbShellAsync(default!, default!, default);
    }

    [TestMethod]
    public void CreateCleanupScript_ConsolidatesEveryConfiguredList()
    {
        string script = DeviceDataCleanupService.CreateCleanupScript(
            ["com.example.app"],
            useRmRfForPackageCleanup: false,
            clearGoogleAccounts: true,
            useDefaultMode: true);

        foreach (string pattern in DeviceDataCleanupService.AccountDirectoryPatterns)
            Assert.Contains(pattern, script, StringComparison.Ordinal);
        foreach (string pattern in DeviceDataCleanupService.AccountFilePatterns)
            Assert.Contains(pattern, script, StringComparison.Ordinal);
        foreach (string pattern in DeviceDataCleanupService.DefaultModeFilePatterns)
            Assert.Contains(pattern, script, StringComparison.Ordinal);
        foreach (string pattern in DeviceDataCleanupService.ResidualDirectoryPatterns)
            Assert.Contains(pattern, script, StringComparison.Ordinal);
        foreach (string pattern in DeviceDataCleanupService.ResidualFilePatterns)
            Assert.Contains(pattern, script, StringComparison.Ordinal);

        Assert.AreEqual(1, CountOccurrences(script, "for package in "));
        Assert.AreEqual(3, CountOccurrences(script, "for target in "));
        Assert.AreEqual(2, CountOccurrences(script, "rm -f /data"));
        Assert.Contains("cmd activity force-stop-all >/dev/null 2>&1 || true", script, StringComparison.Ordinal);
        AssertCleanupFinalization(script);
    }

    [TestMethod]
    public void CreatePackageCleanupCommand_SortsDeduplicatesAndRejectsInjection()
    {
        string command = AdbCleanupCommandBuilder.CreatePackageCleanupCommand(
            ["com.example.two", "com.example.one", "com.example.two"],
            useRmRf: false);

        Assert.StartsWith(
            "for package in com.example.one com.example.two; do",
            command,
            StringComparison.Ordinal);
        Assert.AreEqual(1, CountOccurrences(command, "pm clear"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            AdbCleanupCommandBuilder.CreatePackageCleanupCommand(
                ["com.example.app; reboot"],
                useRmRf: false));
    }

    [TestMethod]
    public void CreatePreserveDirectoryCommand_ExpandsWildcardAndPreservesEveryDirectory()
    {
        const string patterns = "/data/misc/apexdata/com.google.* /data/misc/apns";

        string command = AdbCleanupCommandBuilder.CreatePreserveDirectoryCommand(patterns);

        Assert.StartsWith($"for target in {patterns};", command, StringComparison.Ordinal);
        Assert.Contains(
            "find \"$target\" -mindepth 1 -not -type d -delete",
            command,
            StringComparison.Ordinal);
        Assert.Contains("rm -f \"$target\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf", command, StringComparison.Ordinal);
    }

    [TestMethod]
    public void CreateCleanupScript_ConsolidatesAndroidApexButExcludesWifi()
    {
        string script = DeviceDataCleanupService.CreateCleanupScript(
            [],
            useRmRfForPackageCleanup: false,
            clearGoogleAccounts: false,
            useDefaultMode: false);

        Assert.Contains(
            "/data/misc/apexdata/com.android.*",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "case \"$target\" in /data/misc/apexdata/com.android.wifi) continue",
            script,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void CleanupTargets_PreservePackageRegistrySavedWifiAndDirectoryRoots()
    {
        string[] allPatterns =
        [
            .. DeviceDataCleanupService.AccountDirectoryPatterns,
            .. DeviceDataCleanupService.AccountFilePatterns,
            .. DeviceDataCleanupService.DefaultModeFilePatterns,
            .. DeviceDataCleanupService.ResidualDirectoryPatterns,
            .. DeviceDataCleanupService.ResidualFilePatterns
        ];

        Assert.IsFalse(allPatterns.Any(target => target.Contains("/data/mi_info", StringComparison.Ordinal)));
        Assert.IsFalse(allPatterns.Any(target => target.Equals("/data/app", StringComparison.Ordinal)));
        Assert.IsFalse(allPatterns.Any(target => target.Contains("packages.xml", StringComparison.Ordinal)));
        Assert.IsFalse(allPatterns.Any(target => target.Contains("packages.list", StringComparison.Ordinal)));
        Assert.IsFalse(allPatterns.Any(target => target.Contains("WifiConfigStore", StringComparison.Ordinal)));
        Assert.IsFalse(allPatterns.Any(target => target.Contains("/data/misc/wifi", StringComparison.Ordinal)));
        Assert.IsFalse(allPatterns.Contains(
            "/data/property/persistent_properties",
            StringComparer.Ordinal));
        Assert.Contains(
            "/data/misc/bluetooth",
            allPatterns);
        Assert.Contains(
            "/data/misc/bluedroid",
            allPatterns);
        Assert.Contains(
            "/data/misc/apexdata/com.android.*",
            allPatterns);
        Assert.Contains(
            "/data/misc/apexdata/com.android.wifi",
            DeviceDataCleanupService.ProtectedDirectoryPaths);
        Assert.Contains("/data/misc_ce", DeviceDataCleanupService.ResidualDirectoryPatterns);
        Assert.Contains("/data/misc_de", DeviceDataCleanupService.ResidualDirectoryPatterns);
        Assert.Contains(
            DeviceDataCleanupService.SsaidFilePattern,
            DeviceDataCleanupService.ResidualFilePatterns);
        Assert.IsFalse(DeviceDataCleanupService.ResidualDirectoryPatterns.Any(
            target => target.EndsWith("/*", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CleanupTargets_IncludeAuditedAccountCreatorAndMiChangerState()
    {
        string[] requiredDirectoryPatterns =
        [
            "/data/incremental",
            "/data/local",
            "/data/misc/recovery",
            "/data/misc/update_engine",
            "/data/misc/update_engine_log",
            "/data/misc/user",
            "/data/system/recoverablekeystore",
            "/data/system/storage",
            "/data/system/battery-history",
            "/data/system/blobstore",
            "/data/system/ifw",
            "/data/system/install_sessions*",
            "/data/system/integrity_rules*",
            "/data/misc/credstore",
            "/data/misc/installd",
            "/data/misc/profman",
            "/data/misc/trace"
        ];
        string[] requiredFilePatterns =
        [
            "/data/system/notification-log*",
            "/data/system/sensor_privacy.xml*",
            "/data/system/recoverablekeystore.db*"
        ];

        foreach (string pattern in requiredDirectoryPatterns)
            Assert.Contains(pattern, DeviceDataCleanupService.ResidualDirectoryPatterns);
        foreach (string pattern in requiredFilePatterns)
            Assert.Contains(pattern, DeviceDataCleanupService.ResidualFilePatterns);
    }

    private static DeviceDataCleanupService CreateService(
        IDevicePackageService packages,
        IAdbCommandService? adb = null)
    {
        return new DeviceDataCleanupService(
            packages,
            adb ?? CreateSuccessfulAdb(),
            NullLogger<DeviceDataCleanupService>.Instance);
    }

    private static IAdbCommandService CreateSuccessfulAdb()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellScriptAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        return adb;
    }

    private static string GetOnlyCleanupScript(IAdbCommandService adb)
    {
        string[] scripts = adb.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAdbCommandService.RunAdbShellScriptAsync))
            .Select(call => Assert.IsInstanceOfType<string>(call.GetArguments()[1]))
            .ToArray();
        Assert.HasCount(1, scripts);
        return scripts[0];
    }

    private static void AssertCleanupFinalization(string script)
    {
        Assert.StartsWith(
            "cmd activity force-stop-all >/dev/null 2>&1 || true",
            script,
            StringComparison.Ordinal);
        Assert.Contains("pm trim-caches 999G || exit $?", script, StringComparison.Ordinal);
        Assert.EndsWith("sync || exit $?", script, StringComparison.Ordinal);
    }

    private static void AssertNoUnsafeResidualRemove(string script)
    {
        Assert.DoesNotContain("rm -rf", script, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string pattern)
    {
        return (value.Length - value.Replace(pattern, string.Empty, StringComparison.Ordinal).Length)
            / pattern.Length;
    }
}
