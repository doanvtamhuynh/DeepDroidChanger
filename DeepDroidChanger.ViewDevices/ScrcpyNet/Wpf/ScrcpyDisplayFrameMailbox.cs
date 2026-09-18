using System.Buffers;

namespace ScrcpyNet.Wpf;

internal sealed class ScrcpyDisplayFrameMailbox
{
    internal struct PendingFrame
    {
        public VideoStreamDecoder? SourceDecoder;
        public int Generation;
        public int Width;
        public int Height;
        public byte[]? Buffer;
        public int Length;
    }

    private readonly ArrayPool<byte> bufferPool;
    private readonly object gate = new();
    private VideoStreamDecoder? currentSourceDecoder;
    private int currentGeneration;
    private PendingFrame? pendingFrame;
    private bool renderScheduled;

    public ScrcpyDisplayFrameMailbox(ArrayPool<byte>? bufferPool = null)
    {
        this.bufferPool = bufferPool ?? ArrayPool<byte>.Shared;
    }

    public void SetCurrentSource(VideoStreamDecoder? sourceDecoder, int generation)
    {
        PendingFrame? replaced;
        lock (gate)
        {
            currentSourceDecoder = sourceDecoder;
            currentGeneration = generation;
            replaced = pendingFrame;
            pendingFrame = null;
        }

        if (replaced is PendingFrame frame)
            Return(ref frame);
    }

    public bool TryPublish(
        VideoStreamDecoder sourceDecoder,
        int generation,
        int width,
        int height,
        ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(sourceDecoder);
        if (!ScrcpyDisplay.TryGetFrameLayout(
                width,
                height,
                data.Length,
                out _,
                out int expectedLength))
        {
            return false;
        }

        byte[] buffer = bufferPool.Rent(expectedLength);
        data.CopyTo(buffer.AsSpan(0, expectedLength));

        PendingFrame? replaced = null;
        bool shouldSchedule;
        bool accepted;
        lock (gate)
        {
            if (!ReferenceEquals(sourceDecoder, currentSourceDecoder) ||
                generation != currentGeneration)
            {
                accepted = false;
                shouldSchedule = false;
            }
            else
            {
                accepted = true;
                replaced = pendingFrame;
                pendingFrame = new PendingFrame
                {
                    SourceDecoder = sourceDecoder,
                    Generation = generation,
                    Width = width,
                    Height = height,
                    Buffer = buffer,
                    Length = expectedLength
                };
                shouldSchedule = !renderScheduled;
                renderScheduled = true;
            }
        }

        if (replaced is PendingFrame oldFrame)
            Return(ref oldFrame);


        if (!accepted)
            bufferPool.Return(buffer);

        return shouldSchedule;
    }

    public bool TryTake(out PendingFrame frame)
    {
        lock (gate)
        {
            if (pendingFrame is not PendingFrame pending)
            {
                frame = default;
                return false;
            }

            pendingFrame = null;
            frame = pending;
            return true;
        }
    }

    public bool CompleteRenderAndCheckPending()
    {
        lock (gate)
        {
            if (pendingFrame is not null)
                return true;

            renderScheduled = false;
            return false;
        }
    }

    public void Clear()
    {
        PendingFrame? replaced;
        lock (gate)
        {
            replaced = pendingFrame;
            pendingFrame = null;
        }

        if (replaced is PendingFrame frame)
            Return(ref frame);
    }

    public void AbortScheduledRender()
    {
        PendingFrame? replaced;
        lock (gate)
        {
            renderScheduled = false;
            replaced = pendingFrame;
            pendingFrame = null;
        }

        if (replaced is PendingFrame frame)
            Return(ref frame);
    }

    public void Return(ref PendingFrame frame)
    {
        byte[]? buffer = frame.Buffer;
        frame = default;
        if (buffer is not null)
            bufferPool.Return(buffer);
    }
}
