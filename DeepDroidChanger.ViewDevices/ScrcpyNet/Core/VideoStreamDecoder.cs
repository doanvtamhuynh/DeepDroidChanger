using FFmpeg.AutoGen;
using Serilog;
using System;
using System.IO;
using System.Text;

namespace ScrcpyNet
{
    public unsafe class FrameData : IDisposable
    {
        private int disposed;

        /// <summary>
        /// BGRA32 bytes owned by the decoder's latest-frame slot.
        /// The data remains valid until the decoder replaces or disposes this frame.
        /// Event subscribers must consume it synchronously.
        /// </summary>
        public ReadOnlySpan<byte> Data
        {
            get
            {
                if (Volatile.Read(ref disposed) != 0)
                    throw new ObjectDisposedException(nameof(FrameData));

                return new ReadOnlySpan<byte>(data, length);
            }
        }

        public int Width { get; }
        public int Height { get; }
        public int FrameNumber { get; }
        public AVPixelFormat PixelFormat { get; }

        private readonly byte* data;
        private readonly int length;

        public FrameData(
            byte* data,
            int length,
            int width,
            int height,
            int frameNumber,
            AVPixelFormat pixelFormat)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            int expectedLength = checked(width * height * 4);
            if (length != expectedLength)
            {
                throw new ArgumentException(
                    $"A BGRA32 frame must contain exactly {expectedLength} bytes.",
                    nameof(length));
            }

            this.data = data;
            this.length = length;
            Width = width;
            Height = height;
            FrameNumber = frameNumber;
            PixelFormat = pixelFormat;
        }

        ~FrameData()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            ffmpeg.av_free(data);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public unsafe class VideoStreamDecoder : IDisposable
    {
        public Scrcpy? Scrcpy { get; set; }

        /// <summary>
        /// Number of the last decoded frame.
        /// </summary>
        public int FrameCount { get; private set; }

        public event EventHandler<FrameData>? OnFrame;

        private int disposed;
        private readonly object lastFrameLock = new();
        private SwsContext* swsContext = null;
        private FrameData? lastFrame;

        private readonly AVCodec* codec;
        private readonly AVCodecParserContext* parser;
        private readonly AVCodecContext* ctx;
        private readonly AVFrame* frame;
        private readonly AVPacket* packet;

        private static readonly ILogger log = Log.ForContext<VideoStreamDecoder>();

        public VideoStreamDecoder()
        {
            codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null)
                throw new Exception("Couldn't find AVCodec for AV_CODEC_ID_H264.");

            parser = ffmpeg.av_parser_init((int)codec->id);
            if (parser == null)
                throw new Exception("Couldn't initialize AVCodecParserContext.");
            parser->flags |= ffmpeg.PARSER_FLAG_COMPLETE_FRAMES;

            ctx = ffmpeg.avcodec_alloc_context3(codec);
            if (ctx == null)
                throw new Exception("Couldn't allocate AVCodecContext.");

            int ret = ffmpeg.avcodec_open2(ctx, codec, null);
            if (ret < 0)
                throw new Exception("Couldn't open AVCodecContext.");

            frame = ffmpeg.av_frame_alloc();
            if (frame == null)
                throw new Exception("Couldn't allocate AVFrame.");

            packet = ffmpeg.av_packet_alloc();
            if (packet == null)
                throw new Exception("Couldn't allocate AVPacket.");
        }

        ~VideoStreamDecoder()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Decode(byte[] data, long pts = -1)
        {
            ThrowIfDisposed();
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            Decode(data, data.Length, pts);
        }

        public void Decode(byte[] data, int length, long pts = -1)
        {
            ThrowIfDisposed();
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (length < 0 || length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(length));

            fixed (byte* dataPtr = data)
            {
                byte* ptr = dataPtr;
                int dataSize = length;

                while (dataSize > 0)
                {
                    int consumed = ffmpeg.av_parser_parse2(
                        parser,
                        ctx,
                        &packet->data,
                        &packet->size,
                        ptr,
                        dataSize,
                        pts != -1 ? pts : ffmpeg.AV_NOPTS_VALUE,
                        ffmpeg.AV_NOPTS_VALUE,
                        0);

                    if (consumed < 0)
                    {
                        throw new InvalidOperationException(
                            $"Error while parsing an H264 packet (FFmpeg error {consumed}: {GetErrorMessage(consumed)}).");
                    }

                    if (consumed > dataSize)
                    {
                        throw new InvalidOperationException(
                            $"FFmpeg consumed {consumed} bytes from only {dataSize} available parser bytes.");
                    }

                    if (consumed == 0)
                    {
                        throw new InvalidOperationException(
                            "FFmpeg H264 parser consumed zero bytes while input remained.");
                    }

                    ptr += consumed;
                    dataSize -= consumed;

                    if (packet->size != 0)
                        DecodePacket();
                }
            }
        }

        private void DecodePacket()
        {
            FfmpegSendDrainFlow.SendPacketAndDrain(
                () => ffmpeg.avcodec_send_packet(ctx, packet),
                DrainFrames,
                IsAgain,
                result => CreateFfmpegException(
                    "Error sending a packet for decoding",
                    result));
        }

        private int DrainFrames()
        {
            int drainedFrames = 0;
            while (true)
            {
                int result = ffmpeg.avcodec_receive_frame(ctx, frame);
                if (IsAgain(result) || IsEndOfFile(result))
                    return drainedFrames;
                if (result < 0)
                    throw CreateFfmpegException("Error receiving a decoded frame", result);

                RenderDecodedFrame();
                drainedFrames++;
            }
        }

        private void RenderDecodedFrame()
        {
            FrameCount++;

            int width = frame->width;
            int height = frame->height;
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException(
                    $"The decoded frame dimensions {width}x{height} are invalid.");
            }

            Scrcpy? scrcpy = Scrcpy;
            if (scrcpy != null)
            {
                scrcpy.Width = width;
                scrcpy.Height = height;
            }

            int rowLength = checked(4 * width);
            int destSize = checked(rowLength * height);
            int[] destStride = [rowLength];

            // In my tests the code crashed when we use a C# byte-array (new byte[]).
            byte* destBufferPtr = (byte*)ffmpeg.av_malloc((ulong)destSize);
            if (destBufferPtr == null)
            {
                throw new OutOfMemoryException(
                    $"FFmpeg could not allocate {destSize} bytes for a decoded frame.");
            }

            bool frameOwnsBuffer = false;
            try
            {
                byte*[] dest = { destBufferPtr };

                // This frees the old context if needed, so there is no leak here.
                swsContext = ffmpeg.sws_getCachedContext(
                    swsContext,
                    width,
                    height,
                    ctx->pix_fmt,
                    width,
                    height,
                    AVPixelFormat.AV_PIX_FMT_BGRA,
                    ffmpeg.SWS_BICUBIC,
                    null,
                    null,
                    null);

                if (swsContext == null)
                    throw new InvalidOperationException("Couldn't allocate the FFmpeg SwsContext.");

                int outputSliceHeight = ffmpeg.sws_scale(
                    swsContext,
                    frame->data,
                    frame->linesize,
                    0,
                    height,
                    dest,
                    destStride);

                if (outputSliceHeight != height)
                {
                    throw new InvalidDataException(
                        $"FFmpeg sws_scale returned {outputSliceHeight} rows for a {width}x{height} frame.");
                }

                FrameData currentFrame = new(
                    destBufferPtr,
                    destSize,
                    width,
                    height,
                    ctx->frame_number,
                    AVPixelFormat.AV_PIX_FMT_BGRA);
                frameOwnsBuffer = true;

                FrameData? previousFrame;
                lock (lastFrameLock)
                {
                    previousFrame = lastFrame;
                    lastFrame = currentFrame;
                }

                // The previous frame is no longer reachable through the decoder slot.
                previousFrame?.Dispose();

                InvokeFrameSafely(currentFrame);
            }
            finally
            {
                if (!frameOwnsBuffer)
                    ffmpeg.av_free(destBufferPtr);
            }
        }

        public DecodedFrameSnapshot? CaptureLatestFrame()
        {
            ThrowIfDisposed();

            lock (lastFrameLock)
            {
                FrameData? currentFrame = lastFrame;
                if (currentFrame is null)
                    return null;

                byte[] managedFrame = currentFrame.Data.ToArray();
                return new DecodedFrameSnapshot(
                    currentFrame.Width,
                    currentFrame.Height,
                    managedFrame);
            }
        }

        private void InvokeFrameSafely(FrameData currentFrame)
        {
            EventHandler<FrameData>? handlers = OnFrame;
            if (handlers is null)
                return;

            foreach (EventHandler<FrameData> handler in handlers.GetInvocationList())
            {
                InvokeSubscriberSafely(
                    () => handler(this, currentFrame),
                    exception => log.Warning(
                        exception,
                        "A VideoStreamDecoder.OnFrame subscriber failed."));
            }
        }

        internal static void InvokeSubscriberSafely(
            Action subscriber,
            Action<Exception> logWarning)
        {
            ArgumentNullException.ThrowIfNull(subscriber);
            ArgumentNullException.ThrowIfNull(logWarning);

            try
            {
                subscriber();
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logWarning(exception);
            }
        }

        private static bool IsAgain(int result) => result == ffmpeg.AVERROR(ffmpeg.EAGAIN);

        private static bool IsEndOfFile(int result) => result == ffmpeg.AVERROR_EOF;

        private static Exception CreateFfmpegException(string operation, int result)
        {
            return new InvalidOperationException(
                $"{operation} (FFmpeg error {result}: {GetErrorMessage(result)}).");
        }

        private static unsafe string GetErrorMessage(int result)
        {
            byte[] errorMessageBytes = new byte[512];
            fixed (byte* ptr = errorMessageBytes)
            {
                ffmpeg.av_strerror(result, ptr, (ulong)errorMessageBytes.Length);
                int length = Array.IndexOf(errorMessageBytes, (byte)0);
                if (length < 0)
                    length = errorMessageBytes.Length;
                return Encoding.ASCII.GetString(errorMessageBytes, 0, length);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            FrameData? currentFrame;
            lock (lastFrameLock)
            {
                currentFrame = lastFrame;
                lastFrame = null;
            }

            currentFrame?.Dispose();

            // Free unmanaged resources (unmanaged objects and override finalizer).
            ffmpeg.av_parser_close(parser);
            ffmpeg.sws_freeContext(swsContext);

            fixed (AVCodecContext** ptr = &ctx)
                ffmpeg.avcodec_free_context(ptr);

            fixed (AVFrame** ptr = &frame)
                ffmpeg.av_frame_free(ptr);

            fixed (AVPacket** ptr = &packet)
                ffmpeg.av_packet_free(ptr);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(VideoStreamDecoder));
        }
    }
}
