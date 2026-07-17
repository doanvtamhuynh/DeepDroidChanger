using DeepDroidChanger.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class ProcessRunnerService : IProcessRunnerService
    {
        private readonly ILogger<ProcessRunnerService> _logger;

        public ProcessRunnerService(ILogger<ProcessRunnerService> logger)
        {
            _logger = logger;
        }

        public Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken)
        {
            return RunCoreAsync(fileName, arguments, null, cancellationToken);
        }

        public Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            string standardInput,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(standardInput);
            return RunCoreAsync(fileName, arguments, standardInput, cancellationToken);
        }

        private async Task<CommandResult> RunCoreAsync(
            string fileName,
            string arguments,
            string? standardInput,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug(
                "Starting process {FileName}. ArgumentLength: {ArgumentLength}; StandardInputLength: {StandardInputLength}",
                Path.GetFileName(fileName),
                arguments.Length,
                standardInput?.Length ?? 0);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput != null
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            string output;
            string error;
            try
            {
                if (standardInput != null)
                {
                    await process.StandardInput
                        .WriteAsync(standardInput.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                    process.StandardInput.Close();
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                output = await outputTask.ConfigureAwait(false);
                error = await errorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await TerminateProcessAsync(process).ConfigureAwait(false);
                throw;
            }

            var result = new CommandResult(process.ExitCode, output, error);

            if (result.ExitCode != 0)
                _logger.LogWarning(
                    "Process exited with code {ExitCode}: {FileName}",
                    result.ExitCode,
                    Path.GetFileName(fileName));

            return result;
        }

        private async Task TerminateProcessAsync(Process process)
        {
            try
            {
                if (process.HasExited)
                    return;

                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                _logger.LogWarning(exception, "Failed to terminate canceled process {FileName}.", process.StartInfo.FileName);
            }
        }
    }
}
