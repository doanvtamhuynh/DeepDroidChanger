using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class AdbCommandServiceTests
{
    [TestMethod]
    public async Task RunAdbAsync_SensitiveArguments_AreNotWrittenToLogs()
    {
        const string secret = "proxy-password-should-not-be-logged";
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        var logger = new TestLogger<AdbCommandService>();
        var service = new AdbCommandService(processRunner, logger);

        await service.RunAdbAsync("SERIAL", $"shell setprop proxy.password '{secret}'", CancellationToken.None);

        Assert.IsFalse(logger.Messages.Any(message => message.Contains(secret, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task GetPropertyAsync_FailedAdbRead_DoesNotReturnErrorOutputAsPropertyValue()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, "device unauthorized", "adb failed"));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        string value = await service.GetPropertyAsync(
            "SERIAL",
            "persist.deepdroid.device",
            CancellationToken.None);

        Assert.AreEqual(string.Empty, value);
    }

    [TestMethod]
    public async Task SetPropertyAsync_NonReadOnlyProperty_DoesNotToggleBypass()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        await service.SetPropertyAsync(
            "SERIAL",
            "persist.props.config.product.model",
            "Pixel 9",
            CancellationToken.None);

        await processRunner.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments => arguments.Contains(
                "shell setprop persist.props.config.product.model 'Pixel 9'",
                StringComparison.Ordinal)),
            CancellationToken.None);
        await processRunner.DidNotReceive().RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments => arguments.Contains(
                PropertyConstants.Spoof.BypassReadOnlyProperties,
                StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task SetPropertyAsync_ReadOnlyProperty_TogglesBypassAroundSetprop()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        await service.SetPropertyAsync("SERIAL", "ro.product.model", "Pixel 9", CancellationToken.None);

        Received.InOrder(() =>
        {
            processRunner.RunAsync(
                Arg.Any<string>(),
                Arg.Is<string>(arguments => arguments.Contains(
                    $"shell setprop {PropertyConstants.Spoof.BypassReadOnlyProperties} '1'",
                    StringComparison.Ordinal)),
                CancellationToken.None);
            processRunner.RunAsync(
                Arg.Any<string>(),
                Arg.Is<string>(arguments => arguments.Contains(
                    "shell setprop ro.product.model 'Pixel 9'",
                    StringComparison.Ordinal)),
                CancellationToken.None);
            processRunner.RunAsync(
                Arg.Any<string>(),
                Arg.Is<string>(arguments => arguments.Contains(
                    $"shell setprop {PropertyConstants.Spoof.BypassReadOnlyProperties} '0'",
                    StringComparison.Ordinal)),
                CancellationToken.None);
        });
    }

    [TestMethod]
    public async Task SetPropertyAsync_ReadOnlyPropertyFailure_StillDisablesBypass()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(1).Contains(
                    "setprop ro.product.model",
                    StringComparison.Ordinal)
                ? new CommandResult(1, string.Empty, "setprop failed")
                : new CommandResult(0, string.Empty, string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.SetPropertyAsync(
            "SERIAL",
            "ro.product.model",
            "Pixel 9",
            cancellation.Token));

        await processRunner.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments => arguments.Contains(
                $"shell setprop {PropertyConstants.Spoof.BypassReadOnlyProperties} '0'",
                StringComparison.Ordinal)),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task SetPropertyAsync_BypassDisableFailure_IsReported()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(1).Contains(
                    $"setprop {PropertyConstants.Spoof.BypassReadOnlyProperties} '0'",
                    StringComparison.Ordinal)
                ? new CommandResult(1, string.Empty, "disable failed")
                : new CommandResult(0, string.Empty, string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.SetPropertyAsync(
            "SERIAL",
            "ro.product.model",
            "Pixel 9",
            CancellationToken.None));
    }

    [TestMethod]
    public async Task DeleteSettingAsync_UsesSettingsDeleteCommand()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Deleted 1 rows", string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        await service.DeleteSettingAsync("SERIAL", "secure", "android_id", CancellationToken.None);

        await processRunner.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments => arguments.Contains(
                "shell settings delete secure android_id",
                StringComparison.Ordinal)),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task GetSettingAsync_WifiState_UsesRequestedGlobalSettingCommand()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "1\r\n", string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        string value = await service.GetSettingAsync(
            "SERIAL",
            "global",
            "wifi_on",
            CancellationToken.None);

        Assert.AreEqual("1", value);
        await processRunner.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments => arguments.Contains(
                "shell settings get global wifi_on",
                StringComparison.Ordinal)),
            CancellationToken.None);
    }

    [TestMethod]
    [DataRow(true, "svc wifi enable")]
    [DataRow(false, "svc wifi disable")]
    public async Task SetWifiAsync_UsesRequestedSvcWifiCommand(
        bool enabled,
        string expectedShellCommand)
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        await service.SetWifiAsync("SERIAL", enabled, CancellationToken.None);

        await processRunner.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments => arguments.Contains(
                $"shell {expectedShellCommand}",
                StringComparison.Ordinal)),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task CurlAsync_UsesHttpsSafeFailureAndTimeoutOptions()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "{}", string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        await service.CurlAsync("SERIAL", "https://ipwho.is/", CancellationToken.None);

        await processRunner.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments =>
                arguments.Contains("--fail", StringComparison.Ordinal)
                && arguments.Contains("--show-error", StringComparison.Ordinal)
                && arguments.Contains("--max-time 15", StringComparison.Ordinal)
                && arguments.Contains("'https://ipwho.is/'", StringComparison.Ordinal)),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task RunAdbShellScriptAsync_StreamsNormalizedScriptThroughStandardInput()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        CommandResult result = await service.RunAdbShellScriptAsync(
            "SERIAL",
            "echo first\r\necho second",
            CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        await processRunner.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments =>
                arguments.Contains("-s \"SERIAL\" shell sh", StringComparison.Ordinal)),
            "echo first\necho second\n",
            CancellationToken.None);
        await processRunner.DidNotReceive().RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments => arguments.Contains("push", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }
}
