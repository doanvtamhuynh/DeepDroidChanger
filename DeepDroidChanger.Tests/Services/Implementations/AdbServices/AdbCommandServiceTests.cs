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
}
