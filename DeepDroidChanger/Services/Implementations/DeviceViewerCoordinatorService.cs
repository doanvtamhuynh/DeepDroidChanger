using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceViewerCoordinatorService : IDeviceViewerCoordinatorService
    {
        private const string DeviceStateCommand = "get-state";
        private const string ConnectedDeviceState = "device";
        private const string DeviceSizeCommand = "shell wm size";
        private const char DeviceSizeSeparator = ':';
        private const char DeviceDimensionSeparator = 'x';
        private static readonly char[] DeviceSizeLineSeparators = ['\r', '\n'];
        private const double FallbackDeviceAspectRatio = 9.0 / 20.0;

        private readonly IDeviceViewerStreamService _deviceViewerStreamService;
        private readonly IAdbCommandService _adbCommandService;
        private readonly ILogger<DeviceViewerCoordinatorService> _logger;
        private readonly TimeSpan _reconnectPollInterval;

        public DeviceViewerCoordinatorService(
            IDeviceViewerStreamService deviceViewerStreamService,
            IAdbCommandService adbCommandService,
            ILogger<DeviceViewerCoordinatorService> logger)
            : this(deviceViewerStreamService, adbCommandService, logger, TimeSpan.FromSeconds(3))
        {
        }

        public DeviceViewerCoordinatorService(
            IDeviceViewerStreamService deviceViewerStreamService,
            IAdbCommandService adbCommandService,
            ILogger<DeviceViewerCoordinatorService> logger,
            TimeSpan reconnectPollInterval)
        {
            _deviceViewerStreamService = deviceViewerStreamService;
            _adbCommandService = adbCommandService;
            _logger = logger;
            _reconnectPollInterval = reconnectPollInterval;
        }

        public async Task MonitorStreamAsync(DeviceViewerMonitorContext context)
        {
            try
            {
                var requiresFreshStart = false;
                using var timer = new PeriodicTimer(_reconnectPollInterval);
                while (await timer.WaitForNextTickAsync(context.CancellationToken).ConfigureAwait(true))
                {
                    var isConnected = await IsDeviceConnectedAsync(context.Serial, context.CancellationToken).ConfigureAwait(true);
                    var currentSession = context.GetSession();

                    if (!isConnected)
                    {
                        requiresFreshStart = true;
                        context.BeginNewSession();

                        if (currentSession != null)
                        {
                            await context.StopSessionAsync(currentSession, "device disconnected").ConfigureAwait(true);
                            context.SetSession(null);
                        }

                        await context.MarkWaitingForDeviceAsync().ConfigureAwait(true);
                        await context.MarkDeviceIpUnavailableAsync().ConfigureAwait(true);
                        continue;
                    }

                    if (currentSession is { HasExited: false } && !requiresFreshStart)
                    {
                        context.RequestNativeWindowSync();
                        context.RequestDeviceIpRefresh();
                        continue;
                    }

                    if (currentSession != null)
                    {
                        await context.StopSessionAsync(currentSession, "fresh stream required").ConfigureAwait(true);
                        context.SetSession(null);
                    }

                    requiresFreshStart = false;
                    var generation = context.BeginNewSession();
                    await EnsureStreamAsync(context.ToStartContext(generation)).ConfigureAwait(true);

                    if (context.IsStreaming())
                        context.RequestDeviceIpRefresh();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Device viewer monitor stopped for {Serial}.", context.Serial);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Device viewer monitor error for {Serial}.", context.Serial);
            }
        }

        public async Task EnsureStreamAsync(DeviceViewerStartContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            await context.StartLock.WaitAsync(context.CancellationToken).ConfigureAwait(true);

            try
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var existingSession = context.GetSession();
                if (existingSession is { HasExited: false })
                {
                    context.RequestNativeWindowSync();
                    return;
                }

                if (existingSession != null)
                {
                    await context.StopSessionAsync(existingSession, "stale before start").ConfigureAwait(true);
                    context.SetSession(null);
                }

                if (!await IsDeviceConnectedAsync(context.Serial, context.CancellationToken).ConfigureAwait(true))
                {
                    if (!context.IsCurrentSession(context.Generation))
                        return;

                    context.GetSession()?.SetVisible(false);
                    await context.MarkWaitingForDeviceAsync().ConfigureAwait(true);
                    context.SetSession(null);
                    return;
                }

                await context.MarkStartingAsync().ConfigureAwait(true);

                var aspectRatio = await QueryDeviceAspectRatioAsync(context.Serial, context.CancellationToken).ConfigureAwait(true);
                var startBounds = await context.PrepareStreamBoundsAsync(aspectRatio).ConfigureAwait(true);
                var ownerHwnd = await context.GetOwnerHandleAsync().ConfigureAwait(true);
                if (ownerHwnd == IntPtr.Zero)
                    throw new InvalidOperationException("Device viewer owner window handle is not available.");

                var newSession = await _deviceViewerStreamService
                    .StartAsync(context.Serial, ownerHwnd, startBounds, context.CancellationToken)
                    .ConfigureAwait(true);

                if (!context.IsCurrentSession(context.Generation))
                {
                    newSession.SetVisible(false);
                    await newSession.StopAsync(CancellationToken.None).ConfigureAwait(true);
                    newSession.Dispose();
                    return;
                }

                context.SetSession(newSession);
                await context.MarkStreamingAsync().ConfigureAwait(true);
                context.RequestNativeWindowSync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Device viewer stream start was canceled for {Serial}.", context.Serial);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to start device viewer stream for {Serial}.", context.Serial);
                if (context.IsCurrentSession(context.Generation))
                {
                    context.GetSession()?.SetVisible(false);
                    await context.MarkStreamErrorAsync().ConfigureAwait(true);
                    context.SetSession(null);
                }
            }
            finally
            {
                try
                {
                    context.StartLock.Release();
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogDebug("Device viewer start lock was already disposed for {Serial}.", context.Serial);
                }
            }
        }

        public async Task<bool> IsDeviceConnectedAsync(string serial, CancellationToken cancellationToken)
        {
            try
            {
                var result = await _adbCommandService.RunAdbAsync(serial, DeviceStateCommand, cancellationToken).ConfigureAwait(true);
                return result.ExitCode == 0 &&
                    string.Equals(result.StandardOutput?.Trim(), ConnectedDeviceState, StringComparison.OrdinalIgnoreCase);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Failed to check device state for {Serial}.", serial);
                return false;
            }
        }

        public async Task<double> QueryDeviceAspectRatioAsync(string serial, CancellationToken cancellationToken)
        {
            try
            {
                var result = await _adbCommandService.RunAdbAsync(serial, DeviceSizeCommand, cancellationToken).ConfigureAwait(true);
                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    foreach (var line in result.StandardOutput.Split(DeviceSizeLineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var parts = line.Split(DeviceSizeSeparator, StringSplitOptions.TrimEntries);
                        if (parts.Length < 2)
                            continue;

                        var dims = parts[1].Split(DeviceDimensionSeparator, StringSplitOptions.TrimEntries);
                        if (dims.Length == 2 &&
                            int.TryParse(dims[0], out var width) &&
                            int.TryParse(dims[1], out var height) &&
                            height > 0)
                        {
                            return (double)width / height;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Failed to query device size for {Serial}.", serial);
            }

            return FallbackDeviceAspectRatio;
        }
    }

    public sealed record DeviceViewerStartContext(
        string Serial,
        SemaphoreSlim StartLock,
        CancellationToken CancellationToken,
        Func<IDeviceViewerStreamSession?> GetSession,
        Action<IDeviceViewerStreamSession?> SetSession,
        Func<IDeviceViewerStreamSession, string, Task> StopSessionAsync,
        Action RequestNativeWindowSync,
        long Generation,
        Func<long, bool> IsCurrentSession,
        Func<Task> MarkWaitingForDeviceAsync,
        Func<Task> MarkStartingAsync,
        Func<double, Task<DeviceViewerStreamBounds>> PrepareStreamBoundsAsync,
        Func<Task<IntPtr>> GetOwnerHandleAsync,
        Func<Task> MarkStreamingAsync,
        Func<Task> MarkStreamErrorAsync);

    public sealed record DeviceViewerMonitorContext(
        string Serial,
        SemaphoreSlim StartLock,
        CancellationToken CancellationToken,
        Func<IDeviceViewerStreamSession?> GetSession,
        Action<IDeviceViewerStreamSession?> SetSession,
        Func<IDeviceViewerStreamSession, string, Task> StopSessionAsync,
        Func<long> BeginNewSession,
        Func<long, bool> IsCurrentSession,
        Action RequestNativeWindowSync,
        Action RequestDeviceIpRefresh,
        Func<Task> MarkWaitingForDeviceAsync,
        Func<Task> MarkDeviceIpUnavailableAsync,
        Func<bool> IsStreaming,
        Func<double, Task<DeviceViewerStreamBounds>> PrepareStreamBoundsAsync,
        Func<Task<IntPtr>> GetOwnerHandleAsync,
        Func<Task> MarkStartingAsync,
        Func<Task> MarkStreamingAsync,
        Func<Task> MarkStreamErrorAsync)
    {
        public DeviceViewerStartContext ToStartContext(long generation)
        {
            return new DeviceViewerStartContext(
                Serial,
                StartLock,
                CancellationToken,
                GetSession,
                SetSession,
                StopSessionAsync,
                RequestNativeWindowSync,
                generation,
                IsCurrentSession,
                MarkWaitingForDeviceAsync,
                MarkStartingAsync,
                PrepareStreamBoundsAsync,
                GetOwnerHandleAsync,
                MarkStreamingAsync,
                MarkStreamErrorAsync);
        }
    }
}
