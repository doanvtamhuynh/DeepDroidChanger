using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class AdbDeviceTrackerService : IAdbDeviceTrackerService, IDisposable, IAsyncDisposable
{
    private const string TrackDevicesCommand = "host:track-devices";
    private const int AdbServerPort = 5037;
    private const int FrameHeaderLength = 4;
    private const int MaximumSnapshotLength = 1024 * 1024;
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    private readonly object _gate = new();
    private readonly Dictionary<string, AdbDevice> _snapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly AdbToolPathResolver _toolPathResolver;
    private readonly IProcessRunnerService _processRunner;
    private readonly ILogger<AdbDeviceTrackerService> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TaskCompletionSource<bool> _firstSnapshot = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task? _trackingTask;
    private bool _disposed;
    private AdbDeviceTrackerHealth _health = AdbDeviceTrackerHealth.Reconnecting;

    public AdbDeviceTrackerService(
        AdbToolPathResolver toolPathResolver,
        IProcessRunnerService processRunner,
        ILogger<AdbDeviceTrackerService> logger)
    {
        _toolPathResolver = toolPathResolver;
        _processRunner = processRunner;
        _logger = logger;
    }

    public event EventHandler<AdbDeviceStateChangedEventArgs>? DeviceStateChanged;

    public event EventHandler<AdbDeviceTrackerHealthChangedEventArgs>? HealthChanged;

    public AdbDeviceTrackerHealth Health
    {
        get
        {
            lock (_gate)
                return _health;
        }
    }

    public IReadOnlyList<AdbDevice> CurrentSnapshot
    {
        get
        {
            lock (_gate)
                return _snapshot.Values.ToArray();
        }
    }

    public AdbDevice? GetDevice(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        lock (_gate)
            return _snapshot.TryGetValue(serial, out var device) ? device : null;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _trackingTask ??= TrackDevicesUntilDisposedAsync();
        }

        try
        {
            await _firstSnapshot.Task
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogDebug("ADB device tracker did not receive its first snapshot before the startup timeout.");
        }
    }

    private async Task TrackDevicesUntilDisposedAsync()
    {
        var reconnectAttempt = 0;

        while (!_lifetimeCancellation.IsCancellationRequested)
        {
            SetHealth(AdbDeviceTrackerHealth.Reconnecting);
            var validSnapshotPublished = false;

            try
            {
                using var client = await ConnectToAdbServerAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
                using NetworkStream stream = client.GetStream();

                await SendTrackDevicesRequestAsync(stream, _lifetimeCancellation.Token).ConfigureAwait(false);
                await ReadSnapshotsAsync(
                    stream,
                    _lifetimeCancellation.Token,
                    () =>
                    {
                        validSnapshotPublished = true;
                        reconnectAttempt = 0;
                    }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "ADB host:track-devices connection failed; retaining the last valid snapshot.");
                SetHealth(AdbDeviceTrackerHealth.Reconnecting);
            }

            if (_lifetimeCancellation.IsCancellationRequested)
                break;

            var delayAttempt = GetReconnectDelayAttempt(reconnectAttempt, validSnapshotPublished);
            var delay = ReconnectDelays[delayAttempt];
            reconnectAttempt = AdvanceReconnectAttempt(reconnectAttempt, validSnapshotPublished);

            try
            {
                await Task.Delay(delay, _lifetimeCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<TcpClient> ConnectToAdbServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ConnectOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or TimeoutException)
        {
            _logger.LogDebug(exception, "ADB server was not reachable; starting the canonical ADB server.");
            var result = await _processRunner
                .RunAsync(_toolPathResolver.GetAdbPath(), "start-server", cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("The canonical ADB server could not be started.");

            return await ConnectOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var result = await _processRunner
                .RunAsync(_toolPathResolver.GetAdbPath(), "start-server", cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("The canonical ADB server could not be started.");

            return await ConnectOnceAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<TcpClient> ConnectOnceAsync(CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync("127.0.0.1", AdbServerPort, timeout.Token).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task SendTrackDevicesRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var command = Encoding.ASCII.GetBytes($"{TrackDevicesCommand.Length:X4}{TrackDevicesCommand}");
        await stream.WriteAsync(command, cancellationToken).ConfigureAwait(false);

        var status = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(Encoding.ASCII.GetString(status), "OKAY", StringComparison.Ordinal))
            throw new InvalidOperationException("ADB rejected the host:track-devices request.");
    }

    private async Task ReadSnapshotsAsync(
        NetworkStream stream,
        CancellationToken cancellationToken,
        Action validSnapshotPublished)
    {
        var validSnapshotWasPublished = false;

        while (true)
        {
            var header = await ReadExactAsync(stream, FrameHeaderLength, cancellationToken).ConfigureAwait(false);
            var lengthText = Encoding.ASCII.GetString(header);
            if (!int.TryParse(lengthText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var length) ||
                length < 0 ||
                length > MaximumSnapshotLength)
            {
                throw new InvalidOperationException("ADB returned an invalid host:track-devices frame length.");
            }

            var payload = await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
            var devices = AdbDeviceService.ParseTrackedDevices(Encoding.UTF8.GetString(payload));
            var changes = PublishSnapshot(devices);

            if (!validSnapshotWasPublished)
            {
                validSnapshotWasPublished = true;
                CompleteFirstSnapshot(
                    validSnapshotPublished,
                    () => SetHealth(DetermineHealthAfterTrackResponse(
                        transportAccepted: true,
                        validSnapshotPublished: true)),
                    _firstSnapshot);
            }

            foreach (var change in changes)
                InvokeSafely(DeviceStateChanged, change, "device state");
        }
    }

    internal static int GetReconnectDelayAttempt(int currentAttempt, bool validSnapshotPublished)
    {
        return validSnapshotPublished
            ? 0
            : Math.Min(Math.Max(currentAttempt, 0), ReconnectDelays.Length - 1);
    }

    internal static int AdvanceReconnectAttempt(int currentAttempt, bool validSnapshotPublished)
    {
        var delayAttempt = GetReconnectDelayAttempt(currentAttempt, validSnapshotPublished);
        return Math.Min(delayAttempt + 1, ReconnectDelays.Length - 1);
    }

    internal static AdbDeviceTrackerHealth DetermineHealthAfterTrackResponse(
        bool transportAccepted,
        bool validSnapshotPublished)
    {
        return transportAccepted && validSnapshotPublished
            ? AdbDeviceTrackerHealth.Connected
            : AdbDeviceTrackerHealth.Reconnecting;
    }

    internal static void CompleteFirstSnapshot(
        Action validSnapshotPublished,
        Action setConnected,
        TaskCompletionSource<bool> firstSnapshot)
    {
        ArgumentNullException.ThrowIfNull(validSnapshotPublished);
        ArgumentNullException.ThrowIfNull(setConnected);
        ArgumentNullException.ThrowIfNull(firstSnapshot);

        validSnapshotPublished();
        setConnected();
        firstSnapshot.TrySetResult(true);
    }

    private List<AdbDeviceStateChangedEventArgs> PublishSnapshot(IReadOnlyList<AdbDevice> devices)
    {
        var nextSnapshot = devices
            .GroupBy(device => device.Serial, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        List<AdbDeviceStateChangedEventArgs> changes;

        lock (_gate)
        {
            changes = _snapshot.Keys
                .Concat(nextSnapshot.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(serial =>
                {
                    _snapshot.TryGetValue(serial, out var previous);
                    nextSnapshot.TryGetValue(serial, out var current);
                    return previous?.Status == current?.Status
                        ? null
                        : new AdbDeviceStateChangedEventArgs(serial, previous, current);
                })
                .Where(change => change != null)
                .Cast<AdbDeviceStateChangedEventArgs>()
                .ToList();

            _snapshot.Clear();
            foreach (var device in nextSnapshot)
                _snapshot[device.Key] = device.Value;
        }

        return changes;
    }

    private void SetHealth(AdbDeviceTrackerHealth health)
    {
        AdbDeviceTrackerHealth previous;
        lock (_gate)
        {
            if (_health == health)
                return;

            previous = _health;
            _health = health;
        }

        InvokeSafely(
            HealthChanged,
            new AdbDeviceTrackerHealthChangedEventArgs(previous, health),
            "tracker health");
    }

    private void InvokeSafely<TEventArgs>(
        EventHandler<TEventArgs>? handler,
        TEventArgs eventArgs,
        string eventName)
        where TEventArgs : EventArgs
    {
        if (handler == null)
            return;

        foreach (EventHandler<TEventArgs> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, eventArgs);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "ADB device tracker {EventName} subscriber failed.", eventName);
            }
        }
    }

    private static async Task<byte[]> ReadExactAsync(
        NetworkStream stream,
        int length,
        CancellationToken cancellationToken)
    {
        if (length == 0)
            return Array.Empty<byte>();

        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("ADB closed the host:track-devices connection.");

            offset += read;
        }

        return buffer;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Task? trackingTask;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            trackingTask = _trackingTask;
            _lifetimeCancellation.Cancel();
        }

        if (trackingTask != null)
        {
            try
            {
                await trackingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetimeCancellation.Dispose();
        _firstSnapshot.TrySetCanceled();
    }
}
