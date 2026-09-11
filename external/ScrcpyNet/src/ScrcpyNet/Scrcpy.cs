using Serilog;
using SharpAdbClient;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ScrcpyNet
{
    /// <summary>
    /// A scrcpy client using the protocol implemented by the pinned 1.23 server.
    /// The async lifecycle methods are intentionally kept small so existing
    /// callers can continue using the synchronous Start/Stop wrappers.
    /// </summary>
    public class Scrcpy : IDisposable
    {
        private const int DeviceInfoLength = 68;
        private const int PacketMetadataLength = 12;
        private const int MaximumPacketSize = 64 * 1024 * 1024;
        private static readonly TimeSpan AcceptPollInterval = TimeSpan.FromMilliseconds(10);
        private static readonly TimeSpan BackgroundTaskStopTimeout = TimeSpan.FromSeconds(2);
        private static readonly ILogger Log = Serilog.Log.ForContext<Scrcpy>();

        public string DeviceName { get; private set; } = "";
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public long Bitrate { get; set; } = 8000000;
        public string ScrcpyServerFile { get; set; } = "ScrcpyNet/scrcpy-server.jar";

        public bool Connected => Volatile.Read(ref connected) != 0;

        /// <summary>
        /// The loopback port allocated for the current or most recent start.
        /// It is zero after Stop completes.
        /// </summary>
        public int ListeningPort { get; private set; }

        public VideoStreamDecoder VideoStreamDecoder { get; }

        public event EventHandler<ScrcpyErrorEventArgs>? Failed;
        public event EventHandler? Exited;

        private readonly AdbClient adb;
        private readonly DeviceData device;
        private readonly Channel<IControlMessage> controlChannel = Channel.CreateUnbounded<IControlMessage>();
        private readonly SemaphoreSlim lifecycleGate = new(1, 1);
        private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;

        private TcpClient? videoClient;
        private TcpClient? controlClient;
        private TcpListener? listener;
        private CancellationTokenSource? cts;
        private CancellationTokenSource? serverCancellation;
        private Task? serverTask;
        private Task? videoTask;
        private Task? controlTask;
        private int connected;
        private int intentionalStop = 1;
        private int failureRaised;
        private int disposed;

        public Scrcpy(DeviceData device, VideoStreamDecoder? videoStreamDecoder = null)
        {
            adb = new AdbClient();
            this.device = device;
            VideoStreamDecoder = videoStreamDecoder ?? new VideoStreamDecoder();
            VideoStreamDecoder.Scrcpy = this;
        }

        public void Start(long timeoutMs = 5000)
        {
            StartAsync(timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return StartAsync(5000, cancellationToken);
        }

        public async Task StartAsync(long timeoutMs, CancellationToken cancellationToken = default)
        {
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            ThrowIfDisposed();
            await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (Connected)
                    throw new InvalidOperationException("Already connected.");

                await Task.Run(
                        () => StartCoreAsync(timeoutMs, cancellationToken))
                    .ConfigureAwait(false);
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public void Stop()
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await Task.Run(StopCoreAsync).ConfigureAwait(false);
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public void SendControlCommand(IControlMessage msg)
        {
            if (msg == null)
                throw new ArgumentNullException(nameof(msg));

            if (!Connected || controlClient == null)
            {
                Log.Warning("SendControlCommand() called while the scrcpy control channel is unavailable.");
                return;
            }

            if (!controlChannel.Writer.TryWrite(msg))
                Log.Warning("The scrcpy control channel rejected a control message.");
        }

        private async Task StartCoreAsync(long timeoutMs, CancellationToken cancellationToken)
        {
            Volatile.Write(ref intentionalStop, 0);
            Volatile.Write(ref failureRaised, 0);

            try
            {
                TcpListener currentListener = CreateDynamicLoopbackListener();
                listener = currentListener;
                ListeningPort = ((IPEndPoint)currentListener.LocalEndpoint).Port;

                MobileServerSetup(ListeningPort, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                serverCancellation = new CancellationTokenSource();
                serverTask = MobileServerStart(serverCancellation.Token);
                ObserveServerTask(serverTask);

                TcpClient currentVideoClient = await AcceptClientAsync(
                        currentListener,
                        timeoutMs,
                        "video",
                        cancellationToken)
                    .ConfigureAwait(false);
                videoClient = currentVideoClient;
                TcpClient currentControlClient = await AcceptClientAsync(
                        currentListener,
                        timeoutMs,
                        "control",
                        cancellationToken)
                    .ConfigureAwait(false);
                controlClient = currentControlClient;

                await ReadDeviceInfoAsync(currentVideoClient, cancellationToken).ConfigureAwait(false);

                CancellationTokenSource sessionCancellation = new();
                cts = sessionCancellation;
                Volatile.Write(ref connected, 1);
                videoTask = Task.Run(() => VideoMainAsync(currentVideoClient, sessionCancellation.Token));
                controlTask = Task.Run(() => ControllerMainAsync(currentControlClient, sessionCancellation.Token));

                currentListener.Stop();
                listener = null;

                // The two sockets are established now, so no more reverse
                // connections are needed. Keep the cleanup behavior from the
                // upstream client but isolate failures from the active stream.
                TryMobileServerCleanup();
            }
            catch
            {
                Volatile.Write(ref intentionalStop, 1);
                await StopCoreAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task StopCoreAsync()
        {
            Volatile.Write(ref intentionalStop, 1);
            Volatile.Write(ref connected, 0);

            CancellationTokenSource? sessionCancellation = cts;
            CancellationTokenSource? currentServerCancellation = serverCancellation;
            Task? currentVideoTask = videoTask;
            Task? currentControlTask = controlTask;
            Task? currentServerTask = serverTask;

            try
            {
                sessionCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                currentServerCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                listener?.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                listener = null;
            }

            CloseClient(videoClient);
            CloseClient(controlClient);

            // Removing the reverse rule is what also tells the remote server
            // to finish when a start failed halfway through.
            TryMobileServerCleanup();

            await WaitForBackgroundTaskAsync(currentVideoTask, "video").ConfigureAwait(false);
            await WaitForBackgroundTaskAsync(currentControlTask, "control").ConfigureAwait(false);
            await WaitForBackgroundTaskAsync(currentServerTask, "server").ConfigureAwait(false);

            DisposeClient(videoClient);
            DisposeClient(controlClient);

            videoClient = null;
            controlClient = null;
            videoTask = null;
            controlTask = null;
            serverTask = null;

            if (sessionCancellation != null)
            {
                sessionCancellation.Dispose();
                if (ReferenceEquals(cts, sessionCancellation))
                    cts = null;
            }

            if (currentServerCancellation != null)
            {
                currentServerCancellation.Dispose();
                if (ReferenceEquals(serverCancellation, currentServerCancellation))
                    serverCancellation = null;
            }

            ListeningPort = 0;
        }

        private async Task<TcpClient> AcceptClientAsync(
            TcpListener currentListener,
            long timeoutMs,
            string channelName,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            while (!currentListener.Pending())
            {
                try
                {
                    await Task.Delay(AcceptPollInterval, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Timeout while waiting for the scrcpy {channelName} socket to connect.");
                }
            }

            try
            {
                return currentListener.AcceptTcpClient();
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        internal static TcpListener CreateDynamicLoopbackListener()
        {
            TcpListener value = new(IPAddress.Loopback, 0);
            value.Start();
            return value;
        }

        private async Task ReadDeviceInfoAsync(TcpClient client, CancellationToken cancellationToken)
        {
            NetworkStream infoStream = client.GetStream();
            byte[] deviceInfoBuffer = new byte[DeviceInfoLength];
            await ReadExactAsync(
                    infoStream,
                    deviceInfoBuffer,
                    0,
                    deviceInfoBuffer.Length,
                    cancellationToken)
                .ConfigureAwait(false);

            DeviceName = Encoding.UTF8
                .GetString(deviceInfoBuffer, 0, 64)
                .TrimEnd('\0');
            Width = BinaryPrimitives.ReadUInt16BigEndian(deviceInfoBuffer.AsSpan(64, 2));
            Height = BinaryPrimitives.ReadUInt16BigEndian(deviceInfoBuffer.AsSpan(66, 2));
            Log.Information("Connected to {DeviceName}; initial texture: {Width}x{Height}", DeviceName, Width, Height);
        }

        private async Task VideoMainAsync(TcpClient client, CancellationToken cancellationToken)
        {
            byte[] metadataBuffer = pool.Rent(PacketMetadataLength);
            try
            {
                NetworkStream videoStream = client.GetStream();
                while (!cancellationToken.IsCancellationRequested)
                {
                    await ReadExactAsync(
                            videoStream,
                            metadataBuffer,
                            0,
                            PacketMetadataLength,
                            cancellationToken)
                        .ConfigureAwait(false);

                    ReadOnlySpan<byte> metadata = metadataBuffer.AsSpan(0, PacketMetadataLength);
                    long presentationTimeUs = BinaryPrimitives.ReadInt64BigEndian(metadata);
                    int packetSize = BinaryPrimitives.ReadInt32BigEndian(metadata[8..]);
                    if (packetSize <= 0 || packetSize > MaximumPacketSize)
                        throw new InvalidDataException($"The scrcpy video packet size {packetSize} is invalid.");

                    byte[] packetBuffer = pool.Rent(packetSize);
                    try
                    {
                        await ReadExactAsync(
                                videoStream,
                                packetBuffer,
                                0,
                                packetSize,
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (!cancellationToken.IsCancellationRequested)
                            VideoStreamDecoder.Decode(packetBuffer, packetSize, presentationTimeUs);
                    }
                    finally
                    {
                        pool.Return(packetBuffer);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
            finally
            {
                pool.Return(metadataBuffer);
            }
        }

        private async Task ControllerMainAsync(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                await foreach (IControlMessage command in controlChannel.Reader.ReadAllAsync(cancellationToken))
                    ControllerSend(stream, command);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        private static async Task ReadExactAsync(
            Stream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            int position = offset;
            int remaining = count;
            while (remaining > 0)
            {
                int bytesRead = await stream
                    .ReadAsync(buffer, position, remaining, cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                    throw new EndOfStreamException("The scrcpy socket closed before the expected data was received.");

                position += bytesRead;
                remaining -= bytesRead;
            }
        }

        // This needs to be in a separate method, because we can't use a Span<byte> inside an async function.
        private static void ControllerSend(NetworkStream stream, IControlMessage command)
        {
            byte[] bytes = command.ToBytes().ToArray();
            stream.Write(bytes, 0, bytes.Length);
        }

        private void MobileServerSetup(int hostPort, CancellationToken cancellationToken)
        {
            MobileServerCleanup();
            UploadMobileServer(cancellationToken);
            adb.CreateReverseForward(
                device,
                "localabstract:scrcpy",
                $"tcp:{hostPort}",
                true);
        }

        /// <summary>
        /// Remove ADB forwards/reverses.
        /// </summary>
        private void MobileServerCleanup()
        {
            adb.RemoveAllForwards(device);
            adb.RemoveAllReverseForwards(device);
        }

        private void TryMobileServerCleanup()
        {
            try
            {
                MobileServerCleanup();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "ScrcpyNet reverse/forward cleanup failed for {Serial}.", device.Serial);
            }
        }

        /// <summary>
        /// Start the scrcpy server on the Android device.
        /// </summary>
        private Task MobileServerStart(CancellationToken cancellationToken)
        {
            Log.Information("Starting scrcpy server...");

            SerilogOutputReceiver receiver = new();
            const string version = "1.23";
            const int maxFramerate = 0;
            ScrcpyLockVideoOrientation orientation = ScrcpyLockVideoOrientation.Unlocked;
            const bool control = true;
            const bool showTouches = false;
            const bool stayAwake = false;

            List<string> commands = new()
            {
                "CLASSPATH=/data/local/tmp/scrcpy-server.jar",
                "app_process",
                "/",
                "com.genymobile.scrcpy.Server",
                version,
                "log_level=debug",
                $"bit_rate={Bitrate}",
            };

            if (maxFramerate != 0)
                commands.Add($"max_fps={maxFramerate}");
            if (orientation != ScrcpyLockVideoOrientation.Unlocked)
                commands.Add($"lock_video_orientation={(int)orientation}");

            commands.Add("tunnel_forward=false");
            commands.Add($"control={control}");
            commands.Add("display_id=0");
            commands.Add($"show_touches={showTouches}");
            commands.Add($"stay_awake={stayAwake}");
            commands.Add("power_off_on_close=false");
            commands.Add("downsize_on_error=true");
            commands.Add("cleanup=true");

            string command = string.Join(" ", commands);
            Log.Information("Starting scrcpy server command: {Command}", command);
            return adb.ExecuteRemoteCommandAsync(command, device, receiver, cancellationToken);
        }

        private void UploadMobileServer(CancellationToken cancellationToken)
        {
            using SyncService service = new(
                new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)),
                device);
            using Stream stream = File.OpenRead(ScrcpyServerFile);
            service.Push(stream, "/data/local/tmp/scrcpy-server.jar", 444, DateTime.Now, null, cancellationToken);
        }

        private void ObserveServerTask(Task task)
        {
            _ = task.ContinueWith(
                completedTask =>
                {
                    Exception exception = completedTask.Exception?.GetBaseException()
                        ?? new InvalidOperationException("The scrcpy server task failed without an exception.");
                    ReportFailure(exception);
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void ReportFailure(Exception exception)
        {
            if (Volatile.Read(ref intentionalStop) != 0 ||
                Volatile.Read(ref disposed) != 0 ||
                Interlocked.Exchange(ref failureRaised, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref connected, 0);
            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                serverCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            CloseClient(videoClient);
            CloseClient(controlClient);
            try
            {
                listener?.Stop();
            }
            catch (ObjectDisposedException)
            {
            }

            Log.Error(exception, "ScrcpyNet session failed for {Serial}.", device.Serial);
            InvokeSafely(Failed, new ScrcpyErrorEventArgs(exception));
            InvokeSafely(Exited, EventArgs.Empty);
        }

        private static async Task WaitForBackgroundTaskAsync(Task? task, string taskName)
        {
            if (task == null)
                return;

            Task completed = await Task.WhenAny(task, Task.Delay(BackgroundTaskStopTimeout)).ConfigureAwait(false);
            if (!ReferenceEquals(completed, task))
            {
                Log.Warning("ScrcpyNet {TaskName} task did not stop within the shutdown timeout.", taskName);
                return;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "ScrcpyNet {TaskName} task ended during shutdown.", taskName);
            }
        }

        private static void CloseClient(TcpClient? client)
        {
            try
            {
                client?.Close();
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Failed to close a ScrcpyNet socket.");
            }
        }

        private static void DisposeClient(TcpClient? client)
        {
            try
            {
                client?.Dispose();
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Failed to dispose a ScrcpyNet socket.");
            }
        }

        private static void InvokeSafely<TEventArgs>(
            EventHandler<TEventArgs>? handlers,
            TEventArgs eventArgs)
            where TEventArgs : EventArgs
        {
            if (handlers == null)
                return;

            foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(null, eventArgs);
                }
                catch (Exception exception)
                {
                    Log.Debug(exception, "A ScrcpyNet lifecycle event handler failed.");
                }
            }
        }

        private static void InvokeSafely(EventHandler? handlers, EventArgs eventArgs)
        {
            if (handlers == null)
                return;

            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(null, eventArgs);
                }
                catch (Exception exception)
                {
                    Log.Debug(exception, "A ScrcpyNet lifecycle event handler failed.");
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(Scrcpy));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            try
            {
                StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                VideoStreamDecoder.Dispose();
                lifecycleGate.Dispose();
            }
        }
    }
}
