using DeepDroidChanger.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
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

        public async Task<CommandResult> RunStreamingAsync(string fileName, string arguments,
            Action<ProcessUpdate> output, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName, Arguments = arguments, UseShellExecute = false,
                    CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                }
            };
            process.Start();
            int pid = process.Id;
            var gate = new object();
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            void Publish(ProcessUpdate update) { lock (gate) output(update); }
            async Task ReadAsync(StreamReader reader, StringBuilder text, ProcessUpdateKind kind)
            {
                char[] buffer = new char[4096];
                int count;
                // Drain to EOF after termination as well, preserving the last output.
                while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
                {
                    string chunk = new(buffer, 0, count);
                    text.Append(chunk);
                    Publish(new(kind, pid, chunk));
                }
            }
            Task stdoutTask = Task.CompletedTask;
            Task stderrTask = Task.CompletedTask;
            try
            {
                Publish(new(ProcessUpdateKind.Started, pid));
                stdoutTask = ReadAsync(process.StandardOutput, stdout, ProcessUpdateKind.StandardOutput);
                stderrTask = ReadAsync(process.StandardError, stderr, ProcessUpdateKind.StandardError);
                // A failed consumer must also end the PC process; do not leave a writer alive.
                Task exitTask = process.WaitForExitAsync(cancellationToken);
                var pending = new List<Task> { exitTask, stdoutTask, stderrTask };
                while (pending.Count > 0)
                {
                    Task finished = await Task.WhenAny(pending).ConfigureAwait(false);
                    await finished.ConfigureAwait(false);
                    pending.Remove(finished);
                }
                return new(process.ExitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                await TerminateProcessAsync(process).ConfigureAwait(false);
                try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); }
                finally
                {
                    if (process.HasExited) Publish(new(ProcessUpdateKind.Exited, pid, ExitCode: process.ExitCode));
                }
            }
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
