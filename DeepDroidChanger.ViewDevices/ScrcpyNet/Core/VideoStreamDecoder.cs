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
        /// Byte array with the frame data in BGRA32 format.
        /// WARNING: This field is only valid inside the OnFrame event. After this is finished the Data will be freed.
        /// If you need the Data field outside the OnFrame event then make sure to copy it somewhere.
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

        public FrameData(byte* data, int length, int width, int height, int frameNumber, AVPixelFormat pixelFormat)
        {
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
        private DecodedFrameSnapshot? lastFrame;

        private readonly AVCodec* codec;
        private readonly AVCodecParserContext* parser;
        private readonly AVCodecContext* ctx;
        private readonly AVFrame* frame;
        private readonly AVPacket* packet;

        private static readonly ILogger log = Log.ForContext<VideoStreamDecoder>();

        public VideoStreamDecoder()
        {
            codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null) throw new Exception("Couldn't find AVCodec for AV_CODEC_ID_H264.");

            parser = ffmpeg.av_parser_init((int)codec->id);
            if (parser == null) throw new Exception("Couldn't initialize AVCodecParserContext.");
            parser->flags |= ffmpeg.PARSER_FLAG_COMPLETE_FRAMES;

            ctx = ffmpeg.avcodec_alloc_context3(codec);
            if (ctx == null) throw new Exception("Couldn't allocate AVCodecContext.");

            int ret = ffmpeg.avcodec_open2(ctx, codec, null);
            if (ret < 0) throw new Exception("Couldn't open AVCodecContext.");

            frame = ffmpeg.av_frame_alloc();
            if (frame == null) throw new Exception("Couldn't allocate AVFrame.");

            packet = ffmpeg.av_packet_alloc();
            if (packet == null) throw new Exception("Couldn't allocate AVPacket.");
        }

        ~VideoStreamDecoder()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Decode(byte[] data, long pts = -1)
        {
            ThrowIfDisposed();
            if (data == null) throw new ArgumentNullException(nameof(data));
            Decode(data, data.Length, pts);
        }

        public void Decode(byte[] data, int length, long pts = -1)
        {
            ThrowIfDisposed();
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (length < 0 || length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(length));

            fixed (byte* dataPtr = data)
            {
                byte* ptr = dataPtr;
                int dataSize = length;

                while (dataSize > 0)
                {
                    int ret = ffmpeg.av_parser_parse2(
                        parser,
                        ctx,
                        &packet->data,
                        &packet->size,
                        ptr,
                        dataSize,
                        pts != -1 ? pts : ffmpeg.AV_NOPTS_VALUE,
                        ffmpeg.AV_NOPTS_VALUE,
                        0);

                    if (ret < 0)
                        throw new InvalidOperationException(
                            $"Error while parsing an H264 packet (FFmpeg error {ret}: {GetErrorMessage(ret)}).");

                    ptr += ret;
                    dataSize -= ret;

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

        private void DrainFrames()
        {
            while (true)
            {
                int result = ffmpeg.avcodec_receive_frame(ctx, frame);
                if (IsAgain(result) || IsEndOfFile(result))
                    return;
                if (result < 0)
                    throw CreateFfmpegException("Error receiving a decoded frame", result);

                RenderDecodedFrame();
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

                if (outputSliceHeight <= 0)
                {
                    log.Warning(
                        "FFmpeg sws_scale returned no output for a {Width}x{Height} frame.",
                        width,
                        height);
                    return;
                }

                byte[] managedFrame = new ReadOnlySpan<byte>(destBufferPtr, destSize).ToArray();
                DecodedFrameSnapshot latestFrame = new(width, height, managedFrame);

                FrameData currentFrame = new(
                    destBufferPtr,
                    destSize,
                    width,
                    height,
                    ctx->frame_number,
                    AVPixelFormat.AV_PIX_FMT_BGRA);
                frameOwnsBuffer = true;

                lock (lastFrameLock)
                {
                    lastFrame = latestFrame;
                }

                // FrameData takes ownership of the destBufferPtr and frees it when disposed.
                try
                {
                    OnFrame?.Invoke(this, currentFrame);
                }
                finally
                {
                    currentFrame.Dispose();
                }
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
                return lastFrame;
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

            lock (lastFrameLock)
            {
                lastFrame = null;
            }

            // Free unmanaged resources (unmanaged objects) and override finalizer
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
