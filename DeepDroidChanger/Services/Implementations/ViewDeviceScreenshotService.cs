using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class ViewDeviceScreenshotService : IViewDeviceScreenshotService
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly AdbToolPathResolver _toolPathResolver;
    private readonly ILogger<ViewDeviceScreenshotService> _logger;

    public ViewDeviceScreenshotService(
        AdbToolPathResolver toolPathResolver,
        ILogger<ViewDeviceScreenshotService> logger)
    {
        _toolPathResolver = toolPathResolver;
        _logger = logger;
    }

    public async Task CapturePngAsync(
        string serial,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullDestinationPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The screenshot destination directory does not exist.");

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");
        Process process = new()
        {
            StartInfo = CreateStartInfo(_toolPathResolver.GetAdbPath(), serial),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Failed to start ADB screenshot capture.");

            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await using (FileStream output = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? "ADB could not capture the device screenshot."
                        : $"ADB could not capture the device screenshot: {error.Trim()}");

            await ValidatePngAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            await TerminateAsync(process).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException)
        {
            _logger.LogWarning(exception, "Native device screenshot capture failed for {Serial}.", serial);
            await TerminateAsync(process).ConfigureAwait(false);
            throw;
        }
        finally
        {
            process.Dispose();
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(exception, "Failed to delete a temporary View Device screenshot file.");
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string adbPath, string serial)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = adbPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add(serial);
        startInfo.ArgumentList.Add("exec-out");
        startInfo.ArgumentList.Add("screencap");
        startInfo.ArgumentList.Add("-p");
        return startInfo;
    }

    private static async Task ValidatePngAsync(string path, CancellationToken cancellationToken)
    {
        byte[] signature = new byte[PngSignature.Length];
        await using FileStream input = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: PngSignature.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        int total = 0;
        while (total < signature.Length)
        {
            int read = await input
                .ReadAsync(signature.AsMemory(total, signature.Length - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
        }

        if (total != signature.Length || !signature.SequenceEqual(PngSignature))
            throw new InvalidDataException("ADB returned an invalid PNG screenshot.");
    }

    private static async Task TerminateAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }
}
