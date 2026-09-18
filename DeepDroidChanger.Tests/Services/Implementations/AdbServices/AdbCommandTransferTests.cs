using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class AdbCommandTransferTests
{
    [TestMethod]
    public async Task PushFileAsync_UsesTypedAdbPushArguments()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "pushed", string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        CommandResult result = await service.PushFileAsync(
            "SERIAL",
            @"C:\Temp\payload file.txt",
            "/sdcard/Download/payload file.txt",
            CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        await processRunner.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments => arguments.Contains(
                "-s \"SERIAL\" push \"C:\\Temp\\payload file.txt\" \"/sdcard/Download/payload file.txt\"",
                StringComparison.Ordinal)),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task PullFileAsync_UsesTypedAdbPullArguments()
    {
        IProcessRunnerService processRunner = Substitute.For<IProcessRunnerService>();
        processRunner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "pulled", string.Empty));
        var service = new AdbCommandService(processRunner, new TestLogger<AdbCommandService>());

        CommandResult result = await service.PullFileAsync(
            "SERIAL",
            "/sdcard/Download/payload file.txt",
            @"C:\Temp\payload file.txt",
            CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        await processRunner.Received(1).RunAsync(
            Arg.Any<string>(),
            Arg.Is<string>(arguments => arguments.Contains(
                "-s \"SERIAL\" pull \"/sdcard/Download/payload file.txt\" \"C:\\Temp\\payload file.txt\"",
                StringComparison.Ordinal)),
            CancellationToken.None);
    }
}
