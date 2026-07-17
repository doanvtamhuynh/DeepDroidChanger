using System.Diagnostics;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
[DoNotParallelize]
public sealed class ProcessRunnerServiceTests
{
    [TestMethod]
    public async Task RunAsync_SensitiveArguments_AreNotWrittenToLogs()
    {
        const string secret = "process-secret-should-not-be-logged";
        var logger = new TestLogger<ProcessRunnerService>();
        var service = new ProcessRunnerService(logger);

        await service.RunAsync("cmd.exe", $"/d /c echo {secret}", CancellationToken.None);

        Assert.IsFalse(logger.Messages.Any(message => message.Contains(secret, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RunAsync_StandardInput_IsStreamedWithoutLoggingItsContent()
    {
        const string secret = "stdin-secret-should-not-be-logged";
        var logger = new TestLogger<ProcessRunnerService>();
        var service = new ProcessRunnerService(logger);

        CommandResult result = await service.RunAsync(
            "powershell.exe",
            "-NoProfile -NonInteractive -Command \"[Console]::In.ReadToEnd()\"",
            secret,
            CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains(secret, result.StandardOutput, StringComparison.Ordinal);
        Assert.IsFalse(logger.Messages.Any(message => message.Contains(secret, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RunAsync_Canceled_KillsStartedProcess()
    {
        string pidPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pid");
        var service = new ProcessRunnerService(new TestLogger<ProcessRunnerService>());
        using var cancellation = new CancellationTokenSource();
        string escapedPath = pidPath.Replace("'", "''", StringComparison.Ordinal);
        string arguments = $"-NoProfile -NonInteractive -Command \"$PID | Set-Content -LiteralPath '{escapedPath}'; Start-Sleep -Seconds 30\"";

        Task runTask = service.RunAsync("powershell.exe", arguments, cancellation.Token);
        try
        {
            string pidText = await WaitForFileContentAsync(pidPath, TimeSpan.FromSeconds(15));
            int processId = int.Parse(pidText.Trim(), System.Globalization.CultureInfo.InvariantCulture);

            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => runTask);

            Assert.IsTrue(IsProcessStopped(processId));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }

            File.Delete(pidPath);
        }
    }

    private static async Task<string> WaitForFileContentAsync(string path, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                {
                    string content = await File.ReadAllTextAsync(path, cancellation.Token);
                    if (!string.IsNullOrWhiteSpace(content))
                        return content;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(25, cancellation.Token);
        }
    }

    private static bool IsProcessStopped(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
