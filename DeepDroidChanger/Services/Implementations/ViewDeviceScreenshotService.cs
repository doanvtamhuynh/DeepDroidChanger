using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class ViewDeviceScreenshotService : IViewDeviceScreenshotService
{
    private const uint PngIhdrChunkType = 0x4948_4452;
    private const uint PngIdatChunkType = 0x4944_4154;
    private const uint PngIendChunkType = 0x4945_4E44;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly AdbToolPathResolver _toolPathResolver;
    private readonly ILogger<ViewDeviceScreenshotService> _logger;
    private readonly Func<ProcessStartInfo, IViewDeviceScreenshotProcess> _processFactory;
    private readonly Func<string, Stream> _temporaryOutputFactory;

    public ViewDeviceScreenshotService(
        AdbToolPathResolver toolPathResolver,
        ILogger<ViewDeviceScreenshotService> logger)
        : this(
            toolPathResolver,
            logger,
            static startInfo => new SystemViewDeviceScreenshotProcess(startInfo),
            CreateTemporaryOutput)
    {
    }

    internal ViewDeviceScreenshotService(
        AdbToolPathResolver toolPathResolver,
        ILogger<ViewDeviceScreenshotService> logger,
        Func<ProcessStartInfo, IViewDeviceScreenshotProcess> processFactory,
        Func<string, Stream>? temporaryOutputFactory = null)
    {
        _toolPathResolver = toolPathResolver ?? throw new ArgumentNullException(nameof(toolPathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
        _temporaryOutputFactory = temporaryOutputFactory ?? CreateTemporaryOutput;
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
        IViewDeviceScreenshotProcess? process = null;
        CancellationTokenSource? errorReadCancellation = null;
        Task<string>? errorTask = null;
        bool processStarted = false;

        try
        {
            // Open the destination-side temporary file before starting adb. A
            // path or permission failure must not leave a child process behind.
            await using (Stream output = _temporaryOutputFactory(temporaryPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                process = _processFactory(CreateStartInfo(_toolPathResolver.GetAdbPath(), serial));
                if (!process.Start())
                    throw new InvalidOperationException("Failed to start ADB screenshot capture.");

                processStarted = true;
                errorReadCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                errorTask = process.ReadStandardErrorToEndAsync(errorReadCancellation.Token);
                await process.StandardOutput.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
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
            CancelErrorRead(errorReadCancellation);
            if (processStarted && process is not null)
                await TerminateAsync(process).ConfigureAwait(false);
            await ObserveErrorReadAsync(errorTask).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Native device screenshot capture failed for {Serial}.", serial);
            CancelErrorRead(errorReadCancellation);
            if (processStarted && process is not null)
                await TerminateAsync(process).ConfigureAwait(false);
            await ObserveErrorReadAsync(errorTask).ConfigureAwait(false);
            throw;
        }
        finally
        {
            errorReadCancellation?.Dispose();
            try
            {
                process?.Dispose();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Failed to dispose the ADB screenshot process.");
            }

            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Failed to delete a temporary View Device screenshot file.");
            }
        }
    }

    private static Stream CreateTemporaryOutput(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
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

    private static Task ValidatePngAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using FileStream input = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            ValidatePngStructure(input, cancellationToken);
            input.Position = 0;

            PngBitmapDecoder decoder = new(
                input,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            cancellationToken.ThrowIfCancellationRequested();

            if (decoder.Frames.Count == 0)
            {
                throw new InvalidDataException(
                    "ADB returned a PNG without an image frame.");
            }

            BitmapFrame frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
            {
                throw new InvalidDataException(
                    "ADB returned a PNG with invalid dimensions.");
            }
        }

        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is FileFormatException
                or NotSupportedException
                or IOException
                or ArgumentException)
        {
            throw new InvalidDataException(
                "ADB returned an invalid PNG screenshot.",
                exception);
        }

        return Task.CompletedTask;
    }

    private static void ValidatePngStructure(
        FileStream input,
        CancellationToken cancellationToken)
    {
        Span<byte> signature = stackalloc byte[PngSignature.Length];
        input.ReadExactly(signature);
        if (!signature.SequenceEqual(PngSignature))
            throw new InvalidDataException("ADB returned an invalid PNG signature.");
        bool ihdrSeen = false;
        bool idatSeen = false;
        bool iendSeen = false;
        byte[] chunkBuffer = new byte[64 * 1024];
        byte[] ihdrData = new byte[13];
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> expectedCrcBytes = stackalloc byte[sizeof(uint)];
        while (input.Position < input.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            input.ReadExactly(chunkHeader);
            uint chunkLength = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..4]);
            uint chunkType = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[4..]);
            if (!ihdrSeen && chunkType != PngIhdrChunkType)
            {
                throw new InvalidDataException(
                    "ADB returned a PNG without an IHDR chunk at the start.");
            }
            long bytesAfterHeader = input.Length - input.Position;
            if (bytesAfterHeader < sizeof(uint) ||
                chunkLength > (ulong)(bytesAfterHeader - sizeof(uint)))
            {
                throw new InvalidDataException("ADB returned a truncated PNG chunk.");
            }
            uint crc = UpdatePngCrc(0xFFFF_FFFFu, chunkHeader[4..]);
            if (chunkType == PngIhdrChunkType)
            {
                if (ihdrSeen || chunkLength != ihdrData.Length)
                {
                    throw new InvalidDataException("ADB returned an invalid PNG IHDR chunk.");
                }
                input.ReadExactly(ihdrData);
                crc = UpdatePngCrc(crc, ihdrData);
                uint width = BinaryPrimitives.ReadUInt32BigEndian(ihdrData.AsSpan(0, 4));
                uint height = BinaryPrimitives.ReadUInt32BigEndian(ihdrData.AsSpan(4, 4));
                if (width == 0 || height == 0)
                    throw new InvalidDataException("ADB returned a PNG with invalid dimensions.");
                ihdrSeen = true;
            }
            else
            {
                uint remaining = chunkLength;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int readLength = (int)Math.Min((uint)chunkBuffer.Length, remaining);
                    Span<byte> buffer = chunkBuffer.AsSpan(0, readLength);
                    input.ReadExactly(buffer);
                    crc = UpdatePngCrc(crc, buffer);
                    remaining -= (uint)readLength;
                }
            }
            input.ReadExactly(expectedCrcBytes);
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(expectedCrcBytes);
            if ((crc ^ 0xFFFF_FFFFu) != expectedCrc)
                throw new InvalidDataException("ADB returned a PNG with an invalid chunk CRC.");
            if (chunkType == PngIdatChunkType)
                idatSeen = true;
            else if (chunkType == PngIendChunkType)
            {
                if (chunkLength != 0 || !idatSeen)
                    throw new InvalidDataException("ADB returned an invalid PNG IEND chunk.");
                iendSeen = true;
                break;
            }
        }
        if (!ihdrSeen || !idatSeen || !iendSeen || input.Position != input.Length)
            throw new InvalidDataException("ADB returned an incomplete PNG image.");
    }
    private static uint UpdatePngCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                bool leastBitSet = (crc & 1) != 0;
                crc >>= 1;
                if (leastBitSet)
                    crc ^= 0xEDB8_8320u;
            }
        }
        return crc;
    }

    private async Task TerminateAsync(IViewDeviceScreenshotProcess process)
    {
        bool hasExited;
        try
        {
            hasExited = process.HasExited;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to inspect the ADB screenshot process during cleanup.");
            return;
        }

        if (hasExited)
            return;

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to terminate the ADB screenshot process tree.");
            return;
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to wait for the terminated ADB screenshot process.");
        }
    }

    private void CancelErrorRead(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to cancel ADB screenshot stderr collection.");
        }
    }

    private async Task ObserveErrorReadAsync(Task<string>? errorTask)
    {
        if (errorTask is null)
            return;

        try
        {
            await errorTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "ADB screenshot stderr collection ended during cleanup.");
        }
    }
}

internal interface IViewDeviceScreenshotProcess : IDisposable
{
    Stream StandardOutput { get; }
    bool HasExited { get; }
    int ExitCode { get; }

    bool Start();
    Task<string> ReadStandardErrorToEndAsync(CancellationToken cancellationToken);
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void Kill(bool entireProcessTree);
}

internal sealed class SystemViewDeviceScreenshotProcess : IViewDeviceScreenshotProcess
{
    private readonly Process _process;

    public SystemViewDeviceScreenshotProcess(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
    }

    public Stream StandardOutput => _process.StandardOutput.BaseStream;
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;

    public bool Start() => _process.Start();

    public Task<string> ReadStandardErrorToEndAsync(CancellationToken cancellationToken)
    {
        return _process.StandardError.ReadToEndAsync(cancellationToken);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return _process.WaitForExitAsync(cancellationToken);
    }

    public void Kill(bool entireProcessTree)
    {
        _process.Kill(entireProcessTree);
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
