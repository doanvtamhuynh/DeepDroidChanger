using System.ComponentModel;
using System.Diagnostics;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ViewDeviceScreenshotServiceTests
{
    private const string Serial = "SERIAL-123";
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [TestMethod]
    public async Task TempFileOpenFailure_DoesNotStartAdbProcess()
    {
        using ScreenshotFixture fixture = new();
        FakeScreenshotProcess process = new(ValidPng);
        ViewDeviceScreenshotService service = fixture.CreateService(
            process,
            _ => throw new UnauthorizedAccessException("temporary output denied"));

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
            () => service.CapturePngAsync(Serial, fixture.DestinationPath));

        Assert.AreEqual(0, process.StartCount);
        Assert.AreEqual(0, process.KillCount);
        Assert.AreEqual(0, process.DisposeCount);
        fixture.AssertNoTemporaryFiles();
    }

    [TestMethod]
    public async Task FailureAfterProcessStart_TerminatesWaitsAndDeletesTemporaryFile()
    {
        using ScreenshotFixture fixture = new();
        FakeScreenshotProcess process = new(new ThrowingReadStream());
        ViewDeviceScreenshotService service = fixture.CreateService(process);

        await Assert.ThrowsExactlyAsync<IOException>(
            () => service.CapturePngAsync(Serial, fixture.DestinationPath));

        Assert.AreEqual(1, process.StartCount);
        Assert.AreEqual(1, process.KillCount);
        Assert.AreEqual(1, process.WaitCount);
        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(process.HasExited);
        fixture.AssertNoTemporaryFiles();
    }

    [TestMethod]
    public async Task Win32FailureAfterProcessStart_TerminatesWaitsAndDeletesTemporaryFile()
    {
        using ScreenshotFixture fixture = new();
        FakeScreenshotProcess process = new(
            new ThrowingReadStream(new Win32Exception("native pipe failed")));
        ViewDeviceScreenshotService service = fixture.CreateService(process);

        await Assert.ThrowsExactlyAsync<Win32Exception>(
            () => service.CapturePngAsync(Serial, fixture.DestinationPath));

        Assert.AreEqual(1, process.StartCount);
        Assert.AreEqual(1, process.KillCount);
        Assert.AreEqual(1, process.WaitCount);
        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(process.HasExited);
        fixture.AssertNoTemporaryFiles();
    }

    [TestMethod]
    public async Task Cancellation_TerminatesWaitsDeletesTemporaryFileAndPropagates()
    {
        using ScreenshotFixture fixture = new();
        BlockingReadStream output = new();
        FakeScreenshotProcess process = new(output);
        ViewDeviceScreenshotService service = fixture.CreateService(process);
        using CancellationTokenSource cancellation = new();

        Task capture = service.CapturePngAsync(
            Serial,
            fixture.DestinationPath,
            cancellation.Token);
        await output.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => capture);
        Assert.AreEqual(1, process.StartCount);
        Assert.AreEqual(1, process.KillCount);
        Assert.AreEqual(1, process.WaitCount);
        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(process.HasExited);
        fixture.AssertNoTemporaryFiles();
    }

    [TestMethod]
    public async Task SuccessfulCapture_MovesValidPngAndDisposesProcess()
    {
        using ScreenshotFixture fixture = new();
        FakeScreenshotProcess process = new(ValidPng);
        ViewDeviceScreenshotService service = fixture.CreateService(process);

        await service.CapturePngAsync(Serial, fixture.DestinationPath);

        CollectionAssert.AreEqual(ValidPng, File.ReadAllBytes(fixture.DestinationPath));
        Assert.AreEqual(1, process.StartCount);
        Assert.AreEqual(0, process.KillCount);
        Assert.AreEqual(1, process.WaitCount);
        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(process.HasExited);
        fixture.AssertNoTemporaryFiles();
    }

    [TestMethod]
    public async Task InvalidPng_DoesNotOverwriteDestinationAndDeletesTemporaryFile()
    {
        using ScreenshotFixture fixture = new();
        byte[] original = [10, 20, 30, 40];
        File.WriteAllBytes(fixture.DestinationPath, original);
        FakeScreenshotProcess process = new([1, 2, 3, 4]);
        ViewDeviceScreenshotService service = fixture.CreateService(process);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => service.CapturePngAsync(Serial, fixture.DestinationPath));

        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.DestinationPath));
        Assert.AreEqual(0, process.KillCount);
        Assert.AreEqual(1, process.WaitCount);
        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(process.HasExited);
        fixture.AssertNoTemporaryFiles();
    }

    [TestMethod]
    public async Task AdbNonZeroExit_ThrowsAndDeletesTemporaryFile()
    {
        using ScreenshotFixture fixture = new();
        FakeScreenshotProcess process = new(ValidPng, exitCode: 17, error: "capture failed");
        ViewDeviceScreenshotService service = fixture.CreateService(process);

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.CapturePngAsync(Serial, fixture.DestinationPath));

        StringAssert.Contains(exception.Message, "capture failed");
        Assert.IsFalse(File.Exists(fixture.DestinationPath));
        Assert.AreEqual(0, process.KillCount);
        Assert.AreEqual(1, process.WaitCount);
        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(process.HasExited);
        fixture.AssertNoTemporaryFiles();
    }

    [TestMethod]
    public async Task TruncatedPngWithValidSignature_IsRejected()
    {
        using ScreenshotFixture fixture = new();
        byte[] original = [10, 20, 30, 40];
        File.WriteAllBytes(fixture.DestinationPath, original);
        FakeScreenshotProcess process = new(ValidPng[..^1]);
        ViewDeviceScreenshotService service = fixture.CreateService(process);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => service.CapturePngAsync(Serial, fixture.DestinationPath));

        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.DestinationPath));
        Assert.AreEqual(0, process.KillCount);
        Assert.AreEqual(1, process.WaitCount);
        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(process.HasExited);
        fixture.AssertNoTemporaryFiles();
    }

    [TestMethod]
    public async Task CorruptPngWithValidSignature_IsRejected()
    {
        using ScreenshotFixture fixture = new();
        byte[] original = [10, 20, 30, 40];
        File.WriteAllBytes(fixture.DestinationPath, original);
        FakeScreenshotProcess process = new(CreateCorruptPng());
        ViewDeviceScreenshotService service = fixture.CreateService(process);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => service.CapturePngAsync(Serial, fixture.DestinationPath));

        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.DestinationPath));
        Assert.AreEqual(0, process.KillCount);
        Assert.AreEqual(1, process.WaitCount);
        Assert.AreEqual(1, process.DisposeCount);
        Assert.IsTrue(process.HasExited);
        fixture.AssertNoTemporaryFiles();
    }

    private static byte[] CreateCorruptPng()
    {
        byte[] corrupt = ValidPng.ToArray();
        // The fixture's IDAT payload starts at offset 41; changing it leaves
        // the chunk CRC invalid while keeping the PNG fully structured.
        corrupt[41] ^= 0xFF;
        return corrupt;
    }

    private sealed class ScreenshotFixture : IDisposable
    {
        public ScreenshotFixture()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "DeepDroidChanger.Tests",
                Guid.NewGuid().ToString("N"));
            string adbDirectory = Path.Combine(
                RootPath,
                "Assets",
                "Tools",
                "platform-tools");
            Directory.CreateDirectory(adbDirectory);
            File.WriteAllBytes(Path.Combine(adbDirectory, "adb.exe"), []);
            DestinationPath = Path.Combine(RootPath, "capture.png");
            ToolPathResolver = new AdbToolPathResolver(RootPath, RootPath);
        }

        public string RootPath { get; }
        public string DestinationPath { get; }
        private AdbToolPathResolver ToolPathResolver { get; }

        public ViewDeviceScreenshotService CreateService(
            FakeScreenshotProcess process,
            Func<string, Stream>? temporaryOutputFactory = null)
        {
            return new ViewDeviceScreenshotService(
                ToolPathResolver,
                new TestLogger<ViewDeviceScreenshotService>(),
                _ => process,
                temporaryOutputFactory);
        }

        public void AssertNoTemporaryFiles()
        {
            Assert.IsFalse(Directory.EnumerateFiles(RootPath, "*.tmp").Any());
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class FakeScreenshotProcess : IViewDeviceScreenshotProcess
    {
        private readonly Stream _standardOutput;
        private readonly string _error;

        public FakeScreenshotProcess(
            byte[] standardOutput,
            int exitCode = 0,
            string error = "")
            : this(new MemoryStream(standardOutput, writable: false), exitCode, error)
        {
        }

        public FakeScreenshotProcess(
            Stream standardOutput,
            int exitCode = 0,
            string error = "")
        {
            _standardOutput = standardOutput;
            ExitCode = exitCode;
            _error = error;
        }

        public Stream StandardOutput => _standardOutput;
        public bool HasExited { get; private set; }
        public int ExitCode { get; }
        public int StartCount { get; private set; }
        public int KillCount { get; private set; }
        public int WaitCount { get; private set; }
        public int DisposeCount { get; private set; }

        public bool Start()
        {
            StartCount++;
            return true;
        }

        public Task<string> ReadStandardErrorToEndAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_error);
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitCount++;
            HasExited = true;
            return Task.CompletedTask;
        }

        public void Kill(bool entireProcessTree)
        {
            Assert.IsTrue(entireProcessTree);
            KillCount++;
            HasExited = true;
        }

        public void Dispose()
        {
            DisposeCount++;
            _standardOutput.Dispose();
        }
    }

    private abstract class ReadOnlyTestStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingReadStream : ReadOnlyTestStream
    {
        private readonly Exception _exception;

        public ThrowingReadStream(Exception? exception = null)
        {
            _exception = exception ?? new IOException("stdout copy failed");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw _exception;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return Task.FromException<int>(_exception);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<int>(_exception);
        }
    }

    private sealed class BlockingReadStream : ReadOnlyTestStream
    {
        public TaskCompletionSource ReadEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
