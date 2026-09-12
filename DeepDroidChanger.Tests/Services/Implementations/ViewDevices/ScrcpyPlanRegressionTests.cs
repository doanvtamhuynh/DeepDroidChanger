using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ScrcpyNet;
using ScrcpyNet.Wpf;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ScrcpyPlanRegressionTests
{
    [TestMethod]
    public void VideoPacketHeader_ParsesV123FlagsAndMaskedPresentationTime()
    {
        const ulong configFlag = 1UL << 63;
        const ulong keyFrameFlag = 1UL << 62;
        const long presentationTimeUs = 0x123456789ABCDE;

        ScrcpyVideoPacketHeader header = ScrcpyVideoPacketHeader.Parse(
            CreateHeader(
                configFlag | keyFrameFlag | (ulong)presentationTimeUs,
                0x01020304));

        Assert.IsTrue(header.IsConfig);
        Assert.IsTrue(header.IsKeyFrame);
        Assert.AreEqual(presentationTimeUs, header.PresentationTimeUs);
        Assert.AreEqual(0x01020304, header.PacketSize);
    }

    [TestMethod]
    public void VideoPacketHeader_RejectsZeroAndOversizedPackets()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => ScrcpyVideoPacketHeader.Parse(CreateHeader(0, 0)));
        Assert.ThrowsExactly<InvalidDataException>(
            () => ScrcpyVideoPacketHeader.Parse(
                CreateHeader(0, (uint)ScrcpyVideoPacketHeader.MaximumPacketSize + 1)));
    }

    [TestMethod]
    public void VideoPacketAssembler_ConcatenatesConfigPacketsWithNextVideoPacketAndClears()
    {
        ScrcpyVideoPacketAssembler assembler = new();
        assembler.StoreConfig(new byte[] { 1, 2 });
        assembler.StoreConfig(new byte[] { 3 });

        byte[]? combined = assembler.ConsumeWith(new byte[] { 4, 5 });

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, combined);
        Assert.IsNull(assembler.ConsumeWith(new byte[] { 6 }));

        assembler.StoreConfig(new byte[] { 7 });
        assembler.Clear();
        Assert.IsNull(assembler.ConsumeWith(new byte[] { 8 }));
    }

    [TestMethod]
    public void VideoPacketAssembler_BoundsPendingConfig()
    {
        ScrcpyVideoPacketAssembler assembler = new();

        Assert.ThrowsExactly<InvalidDataException>(
            () => assembler.StoreConfig(
                new byte[ScrcpyVideoPacketAssembler.MaximumPendingConfigSize + 1]));
    }

    [TestMethod]
    public void FfmpegSendDrainFlow_RetriesSamePacketAfterEagainAndDrainsEachAttempt()
    {
        const int eagain = -11;
        int sendCount = 0;
        int drainCount = 0;

        FfmpegSendDrainFlow.SendPacketAndDrain(
            () => ++sendCount == 1 ? eagain : 0,
            () => drainCount++,
            result => result == eagain,
            result => new InvalidOperationException($"unexpected FFmpeg result {result}"));

        Assert.AreEqual(2, sendCount);
        Assert.AreEqual(2, drainCount);
    }

    [TestMethod]
    public void DecodedFrameSnapshot_RequiresExactPositiveBgraDimensions()
    {
        DecodedFrameSnapshot snapshot = new(2, 3, new byte[24]);

        Assert.AreEqual(2, snapshot.Width);
        Assert.AreEqual(3, snapshot.Height);
        Assert.AreEqual(24, snapshot.Bgra32.Length);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new DecodedFrameSnapshot(0, 3, new byte[1]));
        Assert.ThrowsExactly<ArgumentException>(
            () => new DecodedFrameSnapshot(2, 3, new byte[23]));
    }

    [TestMethod]
    public void ScrcpyDisplay_FrameLayoutRejectsMalformedAndOverflowingBuffers()
    {
        Assert.IsTrue(ScrcpyDisplay.TryGetFrameLayout(2, 3, 24, out int rowLength, out int expectedLength));
        Assert.AreEqual(8, rowLength);
        Assert.AreEqual(24, expectedLength);

        Assert.IsFalse(ScrcpyDisplay.TryGetFrameLayout(2, 3, 23, out _, out _));
        Assert.IsFalse(ScrcpyDisplay.TryGetFrameLayout(0, 3, 0, out _, out _));
        Assert.IsFalse(ScrcpyDisplay.TryGetFrameLayout(int.MaxValue, 1, 0, out _, out _));
    }

    [TestMethod]
    public void ScrcpyDisplay_OnlyAcceptsTheCurrentVideoDecoderAsFrameSource()
    {
        VideoStreamDecoder current = CreateUninitializedDecoder();
        VideoStreamDecoder previous = CreateUninitializedDecoder();

        Assert.IsTrue(ScrcpyDisplay.IsCurrentFrameSource(current, current));
        Assert.IsFalse(ScrcpyDisplay.IsCurrentFrameSource(current, previous));
        Assert.IsFalse(ScrcpyDisplay.IsCurrentFrameSource(null, current));
        Assert.IsFalse(ScrcpyDisplay.IsCurrentFrameSource(current, new object()));
    }

    [TestMethod]
    public async Task ScrcpyJoinDecoderTasksAsync_WaitsForVideoAndControlBeforeReturning()
    {
        TaskCompletionSource video = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource control = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task joined = Scrcpy.JoinDecoderTasksAsync(video.Task, control.Task);
        Assert.IsFalse(joined.IsCompleted);

        video.SetResult();
        await Task.Yield();
        Assert.IsFalse(joined.IsCompleted);

        control.SetResult();
        await joined;
    }

    [TestMethod]
    public async Task StartupIo_ReadExactAsync_HandlesPartialReadsWithOneDeadline()
    {
        byte[] expected = [1, 2, 3, 4, 5];
        byte[] actual = new byte[expected.Length];
        using CancellationTokenSource deadline =
            ScrcpyStartupIo.CreateDeadline(1000, CancellationToken.None);

        await ScrcpyStartupIo.ReadExactAsync(
            new ChunkedReadStream(expected, 1),
            actual,
            0,
            actual.Length,
            deadline.Token,
            CancellationToken.None,
            "reading test metadata");

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task StartupIo_ReadExactAsync_ReportsDeadlineTimeoutWithStage()
    {
        using CancellationTokenSource deadline =
            ScrcpyStartupIo.CreateDeadline(50, CancellationToken.None);

        TimeoutException exception = await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => ScrcpyStartupIo.ReadExactAsync(
                new BlockingReadStream(),
                new byte[1],
                0,
                1,
                deadline.Token,
                CancellationToken.None,
                "reading test metadata"));

        StringAssert.Contains(exception.Message, "reading test metadata");
    }

    [TestMethod]
    public async Task StartupIo_ReadExactAsync_PreservesCallerCancellation()
    {
        using CancellationTokenSource caller = new();
        using CancellationTokenSource deadline =
            ScrcpyStartupIo.CreateDeadline(5000, caller.Token);
        BlockingReadStream stream = new();

        Task read = ScrcpyStartupIo.ReadExactAsync(
            stream,
            new byte[1],
            0,
            1,
            deadline.Token,
            caller.Token,
            "reading test metadata");
        await stream.ReadStarted;

        caller.Cancel();
        OperationCanceledException exception =
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => read);

        Assert.AreEqual(caller.Token, exception.CancellationToken);
    }

    [TestMethod]
    public void KeycodeHelper_TryConvertKeyRejectsUnsupportedKeysWithoutUnknownMessages()
    {
        Assert.IsFalse(KeycodeHelper.TryConvertKey(Key.F1, out AndroidKeycode keycode));
        Assert.AreEqual(AndroidKeycode.AKEYCODE_UNKNOWN, keycode);
    }

    private static byte[] CreateHeader(ulong ptsFlags, uint packetSize)
    {
        byte[] header = new byte[ScrcpyVideoPacketHeader.HeaderLength];

        BinaryPrimitives.WriteUInt64BigEndian(header, ptsFlags);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), packetSize);
        return header;
    }

    private static VideoStreamDecoder CreateUninitializedDecoder()
    {
        VideoStreamDecoder decoder =
            (VideoStreamDecoder)RuntimeHelpers.GetUninitializedObject(typeof(VideoStreamDecoder));
        GC.SuppressFinalize(decoder);
        return decoder;
    }

    private sealed class ChunkedReadStream(byte[] data, int chunkSize) : MemoryStream(data, writable: false)
    {
        private readonly int chunkSize = chunkSize;

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(base.Read(buffer, offset, Math.Min(count, chunkSize)));
        }
    }

    private sealed class BlockingReadStream : MemoryStream
    {
        private readonly TaskCompletionSource readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => readStarted.Task;

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            readStarted.TrySetResult();
            return WaitForCancellationAsync(cancellationToken);
        }

        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
