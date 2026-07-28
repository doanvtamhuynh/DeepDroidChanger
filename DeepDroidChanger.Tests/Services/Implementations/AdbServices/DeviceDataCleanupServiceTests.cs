using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class DeviceDataCleanupServiceTests
{
    [TestMethod]
    public async Task CleanAsync_FullChangeWithoutPackageOptions_ResetsOnlySharedIdentityState()
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
        IReadOnlyList<string> commands = GetCleanupCommands(adb);
        string script = JoinCommands(commands);
        foreach (string wifiRoot in DeviceDataCleanupService.PreservedWifiDataRoots)
            Assert.DoesNotContain(wifiRoot, script, StringComparison.Ordinal);
        Assert.Contains(
            DeviceDataCleanupService.CreateRemoveFileCommand(
                DeviceDataCleanupService.SsaidFilePattern),
            commands);
        Assert.DoesNotContain("for target", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pm clear", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pm reset-permissions", script, StringComparison.Ordinal);
        Assert.AreEqual("sync", commands[^1]);
    }

    [TestMethod]
    public async Task CleanPreservingSsaidAsync_NoPackageOptions_DoesNotResetIdentityOrGlobalState()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanPreservingSsaidAsync(
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false,
                ClearGoogleAccounts = false
            },
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "sync" },
            GetCleanupCommands(adb).ToArray());
        await packages.DidNotReceiveWithAnyArgs().GetInstalledPackagesAsync(default!, default);
    }

    [TestMethod]
    public async Task CleanAsync_ClearAllPackages_UsesOneAuditableCommandPairPerInstalledPackage()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(
            [
                "com.example.two",
                "android",
                "com.android.shell",
                "com.example.one",
                "com.android.wifi"
            ]);
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanAsync(
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = true,
                ClearGoogleAccounts = false
            },
            CancellationToken.None);

        IReadOnlyList<string> commands = GetCleanupCommands(adb);
        string script = JoinCommands(commands);
        Assert.Contains("am force-stop \"com.example.one\" >/dev/null 2>&1", commands);
        Assert.Contains("am force-stop \"com.example.two\" >/dev/null 2>&1", commands);
        Assert.Contains("pm clear --user 0 \"com.example.one\"", script, StringComparison.Ordinal);
        Assert.Contains("pm clear --user 0 \"com.example.two\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"android\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"com.android.shell\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"com.android.wifi\"", script, StringComparison.Ordinal);
        Assert.IsFalse(commands.Any(command => command.StartsWith(
            "for pkg in $(pm list packages",
            StringComparison.Ordinal)));
        Assert.Contains("pm reset-permissions", commands);
        Assert.Contains("cmd appops reset --user 0", commands);
        Assert.Contains("pm trim-caches 999G", commands);
    }

    [TestMethod]
    public async Task CleanPreservingSsaidAsync_SelectedPackage_DoesNotResetPermissionsOrIdentity()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(["com.example.selected", "com.example.other"]);
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanPreservingSsaidAsync(
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false,
                ClearSelectedPackages = true,
                ClearGoogleAccounts = false,
                SelectedPackages = ["com.example.selected", "com.example.uninstalled"]
            },
            CancellationToken.None);

        string script = JoinCommands(GetCleanupCommands(adb));
        Assert.Contains("pm clear --user 0 \"com.example.selected\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("com.example.uninstalled", script, StringComparison.Ordinal);
        Assert.DoesNotContain("com.example.other", script, StringComparison.Ordinal);
        Assert.DoesNotContain("settings_ssaid", script, StringComparison.Ordinal);
        Assert.DoesNotContain("com.android.wifi", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pm reset-permissions", script, StringComparison.Ordinal);
        Assert.DoesNotContain("cmd appops reset", script, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CleanAsync_ClearGoogleAccounts_ClearsGooglePackagesAndOnlyAccountStores()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        packages.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(["com.google.android.gms", "com.google.android.gsf.login", "com.example.other"]);
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

        IReadOnlyList<string> commands = GetCleanupCommands(adb);
        string script = JoinCommands(commands);
        Assert.Contains("pm clear --user 0 \"com.google.android.gms\"", script, StringComparison.Ordinal);
        Assert.Contains("pm clear --user 0 \"com.google.android.gsf.login\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("com.example.other", script, StringComparison.Ordinal);
        Assert.Contains(
            DeviceDataCleanupService.CreateRemoveFileCommand(
                "/data/system_ce/0/accounts_ce.db*"),
            commands);
        Assert.Contains(
            DeviceDataCleanupService.CreateRemoveFileCommand(
                "/data/system_de/0/accounts_de.db*"),
            commands);
        Assert.DoesNotContain("com.android.settings", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "rm -f /data/system/sync/accounts.xml",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/data/system_ce ||", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/data/system_de ||", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/data/system/*.db", script, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task CleanPostRebootAsync_DeletesDropBoxFilesButPreservesDirectory()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.CleanPostRebootAsync("SERIAL", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(
                    DeviceDataCleanupService.DropBoxDirectoryPath),
                "sync"
            },
            GetCleanupCommands(adb).ToArray());
    }

    [TestMethod]
    public async Task DeleteSsaidAsync_DeletesOnlyUserZeroSsaidAndSyncs()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        IAdbCommandService adb = CreateSuccessfulAdb();
        var service = CreateService(packages, adb);

        await service.DeleteSsaidAsync("SERIAL", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                DeviceDataCleanupService.CreateRemoveFileCommand(
                    DeviceDataCleanupService.SsaidFilePattern),
                "sync"
            },
            GetCleanupCommands(adb).ToArray());
    }

    [TestMethod]
    public void CreatePackageCleanupCommands_UsesPackageManagerAndNeverDeletesApexData()
    {
        IReadOnlyList<string> commands = DeviceDataCleanupService.CreatePackageCleanupCommands(
            ["com.example.app"],
            useDeepPackageWipe: false);
        string script = JoinCommands(commands);

        Assert.HasCount(2, commands);
        Assert.Contains("am force-stop \"com.example.app\"", commands[0], StringComparison.Ordinal);
        Assert.Contains("pm clear --user 0 \"com.example.app\"", commands[1], StringComparison.Ordinal);
        Assert.AreEqual(
            "pm clear --user 0 \"com.example.app\" >/dev/null 2>&1",
            commands[1]);
        Assert.DoesNotContain("/data/misc/apexdata", script, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf", script, StringComparison.Ordinal);
    }

    [TestMethod]
    public void CreatePackageCleanupCommands_DeepModePreservesPackageDirectories()
    {
        IReadOnlyList<string> commands = DeviceDataCleanupService.CreatePackageCleanupCommands(
            ["com.example.app"],
            useDeepPackageWipe: true);

        Assert.HasCount(2 + DeviceDataCleanupService.DeepWipePackagePathTemplates.Length, commands);
        foreach (string template in DeviceDataCleanupService.DeepWipePackagePathTemplates)
        {
            string path = template.Replace("{package}", "com.example.app", StringComparison.Ordinal);
            Assert.Contains(
                DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(path),
                commands);
        }
        Assert.IsFalse(commands.Any(command => command.Contains("rm -rf", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CreatePackageCleanupCommands_SortsDeduplicatesProtectsCoreAndRejectsInjection()
    {
        IReadOnlyList<string> commands = DeviceDataCleanupService.CreatePackageCleanupCommands(
            [
                "com.example.two",
                "com.example.one",
                "com.example.two",
                "android",
                "com.android.shell",
                "com.android.wifi"
            ],
            useDeepPackageWipe: false);

        Assert.HasCount(4, commands);
        Assert.Contains("com.example.one", commands[0], StringComparison.Ordinal);
        Assert.Contains("com.example.one", commands[1], StringComparison.Ordinal);
        Assert.Contains("com.example.two", commands[2], StringComparison.Ordinal);
        Assert.Contains("com.example.two", commands[3], StringComparison.Ordinal);
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreatePackageCleanupCommands(
                ["com.example.app;reboot"],
                useDeepPackageWipe: false));
    }

    [TestMethod]
    public void DeleteContentsCommands_KeepDirectoryAndRejectBroadOrInjectedTargets()
    {
        Assert.AreEqual(
            "find '/data/misc/bluetooth' -mindepth 1 -not -type d -delete",
            DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(
                "/data/misc/bluetooth"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand("/data/vendor"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand("/data/misc/*"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(
                "/data/misc/bluetooth;reboot"));
        foreach (string wifiRoot in DeviceDataCleanupService.PreservedWifiDataRoots)
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(wifiRoot));
            Assert.ThrowsExactly<ArgumentException>(() =>
                DeviceDataCleanupService.CreateRemoveFileCommand(
                    $"{wifiRoot}/WifiConfigStore.xml*"));
        }
    }

    [TestMethod]
    public void RemoveFileCommand_AllowsOnlyTrailingFilenameWildcardAndProtectsDataRoots()
    {
        Assert.AreEqual(
            "rm -f /data/system/users/0/settings_ssaid.xml*",
            DeviceDataCleanupService.CreateRemoveFileCommand(
                "/data/system/users/0/settings_ssaid.xml*"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateRemoveFileCommand("/data/system"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateRemoveFileCommand("/data/local/tmp"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateRemoveFileCommand("/vendor/file"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateRemoveFileCommand(
                "/data/system/a /data/system/b"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateRemoveFileCommand(
                "/data/system/users/*/settings_ssaid.xml*"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateRemoveFileCommand(
                "/data/system/users/0/*"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DeviceDataCleanupService.CreateRemoveFileCommand(
                "/data/system/users/0/settings_ssaid.*.bak"));
    }

    [TestMethod]
    public void FullChangeTargets_PreserveCriticalPixelExperienceAndroid13State()
    {
        IReadOnlyList<string> commands = DeviceDataCleanupService.CreateCleanupCommands(
            ["com.example.app"],
            useDeepPackageWipe: false,
            clearGoogleAccounts: true,
            clearAllPackages: true);
        string script = JoinCommands(commands);

        string[] forbidden =
        [
            "/data/apex",
            "/data/app",
            "/data/vendor",
            "/data/vendor_ce",
            "/data/vendor_de",
            "/data/misc/credstore",
            "/data/misc/gatekeeper",
            "/data/misc/keychain",
            "/data/misc/keystore",
            "/data/misc/installd",
            "/data/misc/profiles ",
            "/data/property",
            "/data/system/*.db",
            "/data/system/install_sessions",
            "/data/system/integrity_rules",
            "/data/system/package_cache",
            "/data/system/recoverablekeystore",
            "/data/system/storage",
            "package-restrictions.xml",
            "packages.xml",
            "packages.list",
            "runtime-permissions.xml",
            "rm -rf"
        ];

        foreach (string target in forbidden)
            Assert.DoesNotContain(target, script, StringComparison.Ordinal);
        foreach (string wifiRoot in DeviceDataCleanupService.PreservedWifiDataRoots)
            Assert.DoesNotContain(wifiRoot, script, StringComparison.Ordinal);
        Assert.Contains(
            DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(
                "/data/misc/bluedroid"),
            commands);
        Assert.Contains(
            DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(
                "/data/misc/bluetooth"),
            commands);
    }

    [TestMethod]
    public void SelectedPackageCleanup_DoesNotResetGlobalPermissions()
    {
        IReadOnlyList<string> commands = DeviceDataCleanupService.CreateCleanupCommands(
            ["com.example.selected"],
            useDeepPackageWipe: false,
            clearGoogleAccounts: false,
            preserveSsaid: true,
            clearAllPackages: false,
            resetSharedIdentityState: false);
        string script = JoinCommands(commands);

        Assert.Contains("pm clear --user 0 \"com.example.selected\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pm reset-permissions", script, StringComparison.Ordinal);
        Assert.DoesNotContain("cmd appops reset", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pm trim-caches", script, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EveryCleanupAction_IsOneShortSingleLineCommand()
    {
        IReadOnlyList<string> commands = DeviceDataCleanupService.CreateCleanupCommands(
            ["com.example.selected"],
            useDeepPackageWipe: true,
            clearGoogleAccounts: true,
            clearAllPackages: true);

        Assert.IsTrue(commands.Count > 20);
        Assert.IsTrue(commands.All(command => !command.Contains('\n')));
        Assert.IsTrue(commands.All(command => command.Length < 256));
        Assert.IsFalse(commands.Any(command => command.Contains("for target", StringComparison.Ordinal)));
        Assert.IsFalse(commands.Any(command => command.Contains("/*/", StringComparison.Ordinal)));
        Assert.AreEqual(commands.Count, commands.Distinct(StringComparer.Ordinal).Count());
        Assert.HasCount(6, DeviceDataCleanupService.AccountCleanupTargets);
        CollectionAssert.AreEqual(
            new[]
            {
                "/data/system_ce/0/accounts_ce.db*",
                "/data/system_de/0/accounts_de.db*",
                "/data/system/users/0/accounts.db*",
                "/data/system/syncmanager.db*",
                "/data/system/sync",
                "/data/system/users/0/registered_services"
            },
            DeviceDataCleanupService.AccountCleanupTargets
                .Select(target => target.Path)
                .ToArray());

        foreach (string template in DeviceDataCleanupService.DeepWipePackagePathTemplates)
        {
            string path = template.Replace(
                "{package}",
                "com.example.selected",
                StringComparison.Ordinal);
            AssertCommandOccursOnce(
                commands,
                DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(path));
        }

        foreach (string path in DeviceDataCleanupService.IdentityDirectoryPaths)
        {
            AssertCommandOccursOnce(
                commands,
                DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(path));
        }

        foreach (DeviceDataCleanupService.CleanupTarget target
                 in DeviceDataCleanupService.AccountCleanupTargets)
        {
            string expectedCommand = target.Kind switch
            {
                DeviceDataCleanupService.CleanupTargetKind.FilePattern =>
                    DeviceDataCleanupService.CreateRemoveFileCommand(target.Path),
                DeviceDataCleanupService.CleanupTargetKind.DirectoryContents =>
                    DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(target.Path),
                _ => throw new AssertFailedException($"Unsupported cleanup target kind: {target.Kind}")
            };
            AssertCommandOccursOnce(commands, expectedCommand);
        }

        AssertCommandOccursOnce(
            commands,
            DeviceDataCleanupService.CreateRemoveFileCommand(
                DeviceDataCleanupService.SsaidFilePattern));
    }

    [TestMethod]
    public async Task CleanAsync_CommandFailure_IgnoresResultAndRunsEveryCommandOnce()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        string failingCommand = DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(
            DeviceDataCleanupService.IdentityDirectoryPaths[1]);
        adb.RunAdbShellScriptAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                string.Equals(callInfo.ArgAt<string>(1), failingCommand, StringComparison.Ordinal)
                    ? new CommandResult(1, string.Empty, "permission denied")
                    : new CommandResult(0, string.Empty, string.Empty));
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

        IReadOnlyList<string> commands = GetCleanupCommands(adb);
        Assert.IsTrue(commands.Count > 3);
        AssertCommandOccursOnce(commands, failingCommand);
        Assert.AreEqual("sync", commands[^1]);
        await adb.Received(1).RunAdbShellScriptAsync(
            "SERIAL",
            failingCommand,
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().RunAdbShellAsync(
            "SERIAL",
            DeviceChangeConstants.RootIdentityCommand,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task CleanAsync_CommandTimeoutAfterSixtySeconds_ContinuesWithoutRetry()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(60), DeviceDataCleanupService.CleanupCommandTimeout);

        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        string timedOutCommand = DeviceDataCleanupService.CreateDeleteDirectoryContentsCommand(
            DeviceDataCleanupService.IdentityDirectoryPaths[0]);
        adb.RunAdbShellScriptAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (string.Equals(
                        callInfo.ArgAt<string>(1),
                        timedOutCommand,
                        StringComparison.Ordinal))
                {
                    throw new OperationCanceledException(callInfo.ArgAt<CancellationToken>(2));
                }

                return new CommandResult(0, string.Empty, string.Empty);
            });
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

        IReadOnlyList<string> commands = GetCleanupCommands(adb);
        AssertCommandOccursOnce(commands, timedOutCommand);
        Assert.AreEqual("sync", commands[^1]);
    }

    [TestMethod]
    public async Task CleanAsync_ExternalCancellationDuringCommand_Propagates()
    {
        IDevicePackageService packages = Substitute.For<IDevicePackageService>();
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        using var cancellationSource = new CancellationTokenSource();
        adb.RunAdbShellScriptAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellationSource.Cancel();
                if (cancellationSource.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationSource.Token);

                return new CommandResult(0, string.Empty, string.Empty);
            });
        var service = CreateService(packages, adb);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.CleanAsync(
                "SERIAL",
                new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ClearAllPackages = false,
                    ClearGoogleAccounts = false
                },
                cancellationSource.Token));
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

    private static IReadOnlyList<string> GetCleanupCommands(IAdbCommandService adb)
    {
        return adb.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAdbCommandService.RunAdbShellScriptAsync))
            .Select(call => Assert.IsInstanceOfType<string>(call.GetArguments()[1]))
            .ToArray();
    }

    private static void AssertCommandOccursOnce(
        IReadOnlyCollection<string> commands,
        string expectedCommand)
    {
        Assert.AreEqual(
            1,
            commands.Count(command => string.Equals(command, expectedCommand, StringComparison.Ordinal)),
            $"Cleanup command should occur exactly once: {expectedCommand}");
    }

    private static string JoinCommands(IEnumerable<string> commands)
    {
        return string.Join('\n', commands);
    }
}
