using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

internal enum DeviceViewerRuntimeState
{
    Created,
    Starting,
    Streaming,
    WaitingForDevice,
    Reconnecting,
    Closing,
    Closed
}

internal sealed class DeviceViewerRuntime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly DeviceViewerDialog _window;
    private readonly DeviceViewerViewModel _viewModel;
    private readonly string _serial;
    private readonly IServiceScope _ownedScope;
    private readonly IDeviceViewerStreamService _streamService;
    private readonly IDeviceViewerCoordinatorService _coordinator;
    private readonly IAdbDeviceTrackerService _deviceTracker;
    private readonly ILogger<DeviceViewerRuntime> _logger;
    private readonly Action<DeviceViewerRuntime> _removeFromRegistry;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly DeviceViewerLifecycleTransitionGate _transitionGate = new();
    private readonly DeviceViewerNativeWindowSyncScheduler _nativeWindowSyncScheduler;
    private readonly DeviceViewerActiveTaskTracker _sessionStopTasks = new();
    private readonly DeviceViewerIpRefreshLifetime _deviceIpRefreshLifetime;
    private readonly DeviceViewerReconnectBackoff _reconnectBackoff = new();

    private DeviceViewerRuntimeState _state = DeviceViewerRuntimeState.Created;
    private IDeviceViewerStreamSession? _session;
    private CancellationTokenSource? _startAttemptCancellation;
    private CancellationTokenSource? _reconnectCancellation;
    private Task? _initialStartTask;
    private Task? _reconnectTask;
    private Task? _deviceIpTask;
    private Task? _stabilityTask;
    private Task? _closeTask;
    private CancellationTokenSource? _stabilityCancellation;
    private long _generation;
    private bool _closed;

    public DeviceViewerRuntime(
        DeviceViewerDialog window,
        DeviceViewerViewModel viewModel,
        string serial,
        IServiceScope ownedScope,
        IDeviceViewerStreamService streamService,
        IDeviceViewerCoordinatorService coordinator,
        IAdbDeviceTrackerService deviceTracker,
        ILogger<DeviceViewerRuntime> logger,
        Action<DeviceViewerRuntime> removeFromRegistry)
    {
        _window = window;
        _viewModel = viewModel;
        _serial = serial;
        _ownedScope = ownedScope;
        _streamService = streamService;
        _coordinator = coordinator;
        _deviceTracker = deviceTracker;
        _logger = logger;
        _removeFromRegistry = removeFromRegistry;
        _nativeWindowSyncScheduler = new(SynchronizeNativeWindowOnDispatcherAsync);
        _deviceIpRefreshLifetime = new(_lifetimeCancellation.Token);

        _window.ViewerBoundsReady += OnViewerBoundsReady;
        _window.ViewerBoundsChanged += OnViewerBoundsChanged;
        _window.ViewerVisibilityChanged += OnViewerVisibilityChanged;
        _window.Closing += OnWindowClosing;
        _window.Closed += OnWindowClosed;
    }

    public DeviceViewerDialog Window => _window;

    public bool IsLive
    {
        get
        {
            lock (_gate)
                return !_closed;
        }
    }

    internal DeviceViewerRuntimeState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public Task StartAsync()
    {
        lock (_gate)
        {
            if (_initialStartTask != null)
                return _initialStartTask;

            _initialStartTask = StartInitialStreamAsync();
            return _initialStartTask;
        }
    }

    public Task CloseAsync()
    {
        lock (_gate)
        {
            if (_closeTask != null)
                return _closeTask;

            _closeTask = CloseCoreAsync();
            return _closeTask;
        }
    }

    private async Task StartInitialStreamAsync()
    {
        try
        {
            _deviceTracker.DeviceStateChanged += OnDeviceStateChanged;
            _deviceTracker.HealthChanged += OnTrackerHealthChanged;
            await _deviceTracker.StartAsync(_lifetimeCancellation.Token).ConfigureAwait(false);

            if (IsClosed())
                return;

            if (CanStartStream())
            {
                await SetDeviceConnectionStateAsync(DeviceConnectionState.Online).ConfigureAwait(false);
                await RunLifecycleTransitionAsync(
                    () => StartStreamAsync("initial startup")).ConfigureAwait(false);
            }
            else if (IsTrackerHealthy())
            {
                await SetDeviceConnectionStateAsync(ToConnectionState(GetDeviceStatus())).ConfigureAwait(false);
                await EnterWaitingForDeviceAsync().ConfigureAwait(false);
            }
            else
            {
                await MarkTrackerUnavailableAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (IsClosed() || _lifetimeCancellation.IsCancellationRequested)
        {
            _logger.LogDebug("Device viewer initial start was canceled for {Serial}.", _serial);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Device viewer initial start failed for {Serial}.", _serial);
            if (!IsClosed())
            {
                RegisterReconnectFailure(_reconnectBackoff);
                await MarkStreamErrorAsync().ConfigureAwait(false);
                ScheduleReconnectIfAllowed();
            }
        }
    }

    private async Task RunLifecycleTransitionAsync(Func<Task> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        // A callback captured just before unsubscription must be harmless after close.
        await _transitionGate.RunAsync(async () =>
        {
            if (!IsClosed())
                await transition().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task StartStreamAsync(string reason)
    {
        var startGateEntered = false;
        await _startGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
        startGateEntered = true;
        CancellationTokenSource? attemptCancellation = null;
        Task? staleSessionStopTask = null;
        IDeviceViewerStreamSession? staleSession = null;
        long generation = 0;

        try
        {
            lock (_gate)
            {
                if (_closed)
                    return;

                if (_session is { HasExited: false })
                {
                    _state = DeviceViewerRuntimeState.Streaming;
                    return;
                }

                if (_session != null)
                {
                    var sessionToStop = _session;
                    staleSession = sessionToStop;
                    _session = null;
                    sessionToStop.Exited -= OnSessionExited;
                    sessionToStop.SetVisible(false);
                }

                generation = ++_generation;
                _state = reason == "initial startup"
                    ? DeviceViewerRuntimeState.Starting
                    : DeviceViewerRuntimeState.Reconnecting;
                attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
                _startAttemptCancellation = attemptCancellation;
            }

            _deviceIpRefreshLifetime.CancelCurrent();
            if (staleSession != null)
            {
                staleSessionStopTask = TrackSessionStopTask(staleSession, "stale session before restart");
            }

            if (staleSessionStopTask != null)
                await staleSessionStopTask.ConfigureAwait(false);

            if (!IsCurrentGeneration(generation))
                return;

            if (!CanStartStream())
            {
                if (IsTrackerHealthy())
                    await EnterWaitingForDeviceAsync().ConfigureAwait(false);
                else
                    await MarkTrackerUnavailableAsync().ConfigureAwait(false);
                return;
            }

            await MarkStartingAsync(generation).ConfigureAwait(false);
            if (!IsCurrentGeneration(generation))
                return;

            var aspectRatio = await _coordinator
                .QueryDeviceAspectRatioAsync(_serial, attemptCancellation!.Token)
                .ConfigureAwait(false);
            if (!IsCurrentGeneration(generation))
                return;

            var bounds = await PrepareStreamBoundsAsync(aspectRatio, generation).ConfigureAwait(false);
            if (!IsCurrentGeneration(generation))
                return;

            var ownerHandle = await GetOwnerHandleAsync().ConfigureAwait(false);
            if (!IsCurrentGeneration(generation))
                return;

            if (ownerHandle == IntPtr.Zero)
                throw new InvalidOperationException("Device viewer owner window handle is not available.");

            var newSession = await _streamService
                .StartAsync(_serial, ownerHandle, bounds, attemptCancellation!.Token)
                .ConfigureAwait(false);
            if (!IsCurrentGeneration(generation))
            {
                await StopAndDisposeSessionAsync(newSession, "stale stream start").ConfigureAwait(false);
                return;
            }

            newSession.Exited += OnSessionExited;

            var publish = false;
            lock (_gate)
            {
                if (!_closed && generation == _generation && CanStartStream())
                {
                    _session = newSession;
                    _state = DeviceViewerRuntimeState.Streaming;
                    publish = true;
                }
            }

            if (!publish)
            {
                newSession.Exited -= OnSessionExited;
                await StopAndDisposeSessionAsync(newSession, "stale stream start").ConfigureAwait(false);
                return;
            }

            await MarkStreamingAsync(generation).ConfigureAwait(false);
            if (!IsCurrentGeneration(generation))
                return;

            StartStableSessionTimer(newSession, generation);
            _ = ObserveBackgroundTaskAsync(
                _nativeWindowSyncScheduler.RequestAsync(),
                "native-window synchronization");
            var deviceIpTask = RefreshDeviceIpAsync(
                showCheckingState: true,
                sessionGeneration: generation);
            lock (_gate)
                _deviceIpTask = deviceIpTask;
            _ = ObserveBackgroundTaskAsync(deviceIpTask, "device IP refresh");

            if (newSession.HasExited)
                OnSessionExited(newSession, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (IsClosed() || _lifetimeCancellation.IsCancellationRequested)
        {
            _logger.LogDebug("Device viewer stream start was canceled for {Serial}.", _serial);
        }
        catch (OperationCanceledException)
        {
            if (IsClosed() || !IsCurrentGeneration(generation))
                return;

            if (IsTrackerHealthy())
                await EnterWaitingForDeviceAsync().ConfigureAwait(false);
            else
                await MarkTrackerUnavailableAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Device viewer stream start failed for {Serial}. Reason: {Reason}", _serial, reason);
            var shouldReconnect = false;
            lock (_gate)
            {
                if (!_closed && generation == _generation)
                {
                    _state = DeviceViewerRuntimeState.Reconnecting;
                    shouldReconnect = CanStartStream();
                }
            }

            RegisterReconnectFailure(_reconnectBackoff);
            if (IsCurrentGeneration(generation))
                await MarkStreamErrorAsync(generation).ConfigureAwait(false);
            if (shouldReconnect)
                ScheduleReconnectIfAllowed();
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_startAttemptCancellation, attemptCancellation))
                    _startAttemptCancellation = null;
            }

            attemptCancellation?.Dispose();
            if (startGateEntered)
                _startGate.Release();
        }
    }

    private void OnDeviceStateChanged(object? sender, AdbDeviceStateChangedEventArgs eventArgs)
    {
        if (!string.Equals(eventArgs.Serial, _serial, StringComparison.OrdinalIgnoreCase))
            return;

        _ = ObserveBackgroundTaskAsync(
            RunLifecycleTransitionAsync(() => HandleDeviceStateChangedAsync(eventArgs)),
            "device state transition");
    }

    private async Task HandleDeviceStateChangedAsync(AdbDeviceStateChangedEventArgs eventArgs)
    {
        if (IsClosed())
            return;

        if (eventArgs.Current?.Status == AdbDeviceStatus.Online)
        {
            await SetDeviceConnectionStateAsync(DeviceConnectionState.Online).ConfigureAwait(false);
            if (CanStartStream() && !IsStreaming())
                ScheduleReconnectIfAllowed();
            return;
        }

        CancellationTokenSource? reconnectCancellation;
        IDeviceViewerStreamSession? session;
        Task? sessionStopTask = null;
        long generation;
        lock (_gate)
        {
            if (_closed)
                return;

            ++_generation;
            generation = _generation;
            _state = DeviceViewerRuntimeState.WaitingForDevice;
            reconnectCancellation = _reconnectCancellation;
            session = _session;
            _session = null;
            if (session != null)
            {
                session.Exited -= OnSessionExited;
                session.SetVisible(false);
            }
        }

        CancelSafely(reconnectCancellation);
        CancelStartAttempt();
        _deviceIpRefreshLifetime.CancelCurrent();
        CancelStableSessionTimer();

        if (session != null)
        {
            sessionStopTask = TrackSessionStopTask(session, "device is not online");
            await sessionStopTask!.ConfigureAwait(false);
        }

        if (!IsCurrentGeneration(generation))
            return;

        await SetDeviceConnectionStateAsync(ToConnectionState(eventArgs.Current?.Status), generation).ConfigureAwait(false);
        await MarkWaitingForDeviceAsync(generation).ConfigureAwait(false);
        await MarkDeviceIpUnavailableAsync(generation).ConfigureAwait(false);
    }

    private void OnSessionExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not IDeviceViewerStreamSession session)
            return;

        _ = ObserveBackgroundTaskAsync(
            RunLifecycleTransitionAsync(() => HandleSessionExitedAsync(session)),
            "scrcpy exit recovery");
    }

    private async Task HandleSessionExitedAsync(IDeviceViewerStreamSession exitedSession)
    {
        Task? sessionStopTask = null;
        long generation;
        lock (_gate)
        {
            if (_closed || !ReferenceEquals(_session, exitedSession))
                return;

            ++_generation;
            generation = _generation;
            _session = null;
            _state = HasOnlineSnapshot()
                ? DeviceViewerRuntimeState.Reconnecting
                : DeviceViewerRuntimeState.WaitingForDevice;
            exitedSession.Exited -= OnSessionExited;
            exitedSession.SetVisible(false);
        }

        _deviceIpRefreshLifetime.CancelCurrent();
        CancelStableSessionTimer();
        sessionStopTask = TrackSessionStopTask(exitedSession, "scrcpy process exited");
        await sessionStopTask!.ConfigureAwait(false);

        if (!IsCurrentGeneration(generation))
            return;

        if (CanStartStream())
        {
            RegisterReconnectFailure(_reconnectBackoff);
            await MarkStreamErrorAsync(generation).ConfigureAwait(false);
            ScheduleReconnectIfAllowed();
        }
        else if (HasOnlineSnapshot())
        {
            await MarkTrackerUnavailableAsync(generation).ConfigureAwait(false);
        }
        else
        {
            await SetDeviceConnectionStateAsync(ToConnectionState(GetDeviceStatus()), generation).ConfigureAwait(false);
            await EnterWaitingForDeviceAsync(generation).ConfigureAwait(false);
        }
    }

    private void OnTrackerHealthChanged(object? sender, AdbDeviceTrackerHealthChangedEventArgs eventArgs)
    {
        _ = ObserveBackgroundTaskAsync(
            RunLifecycleTransitionAsync(() => HandleTrackerHealthChangedAsync(eventArgs)),
            "tracker health transition");
    }

    private async Task HandleTrackerHealthChangedAsync(AdbDeviceTrackerHealthChangedEventArgs eventArgs)
    {
        if (IsClosed())
            return;

        if (eventArgs.Current == AdbDeviceTrackerHealth.Reconnecting)
        {
            if (IsStreaming())
                return;

            CancelReconnect();
            CancelStartAttempt();
            if (HasOnlineSnapshot())
                await MarkTrackerUnavailableAsync().ConfigureAwait(false);
            else if (GetDeviceStatus() is { } status)
                await SetDeviceConnectionStateAsync(ToConnectionState(status)).ConfigureAwait(false);
            else
                await SetDeviceConnectionStateAsync(DeviceConnectionState.Checking).ConfigureAwait(false);
            return;
        }

        if (!HasOnlineSnapshot())
        {
            await SetDeviceConnectionStateAsync(ToConnectionState(GetDeviceStatus())).ConfigureAwait(false);
            await EnterWaitingForDeviceAsync().ConfigureAwait(false);
            return;
        }

        await SetDeviceConnectionStateAsync(DeviceConnectionState.Online).ConfigureAwait(false);
        if (!IsStreaming())
            ScheduleReconnectIfAllowed();
    }

    private void ScheduleReconnectIfAllowed()
    {
        if (!IsTrackerHealthy())
        {
            _ = ObserveBackgroundTaskAsync(MarkTrackerUnavailableAsync(), "tracker recovery wait");
            return;
        }

        if (!HasOnlineSnapshot())
        {
            _ = ObserveBackgroundTaskAsync(EnterWaitingForDeviceAsync(), "device waiting transition");
            return;
        }

        lock (_gate)
        {
            if (_closed || _reconnectTask is { IsCompleted: false })
                return;

            _state = DeviceViewerRuntimeState.Reconnecting;
            _reconnectCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            _reconnectTask = RunReconnectLoopAsync(_reconnectCancellation);
        }
    }

    private async Task RunReconnectLoopAsync(CancellationTokenSource reconnectCancellation)
    {
        try
        {
            while (true)
            {
                var delay = GetReconnectDelay(_reconnectBackoff);
                await Task.Delay(delay, reconnectCancellation.Token).ConfigureAwait(false);

                if (!IsTrackerHealthy())
                {
                    await MarkTrackerUnavailableAsync().ConfigureAwait(false);
                    return;
                }

                if (!HasOnlineSnapshot())
                {
                    await EnterWaitingForDeviceAsync().ConfigureAwait(false);
                    return;
                }

                await RunLifecycleTransitionAsync(
                    () => StartStreamAsync("reconnect")).ConfigureAwait(false);
                if (IsStreaming())
                    return;
            }
        }
        catch (OperationCanceledException) when (reconnectCancellation.IsCancellationRequested)
        {
            _logger.LogDebug("Device viewer reconnect was canceled for {Serial}.", _serial);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_reconnectCancellation, reconnectCancellation))
                {
                    _reconnectCancellation = null;
                    _reconnectTask = null;
                }
            }

            reconnectCancellation.Dispose();
        }
    }

    private async Task EnterWaitingForDeviceAsync(long? expectedGeneration = null)
    {
        _deviceIpRefreshLifetime.CancelCurrent();

        lock (_gate)
        {
            if (_closed || (expectedGeneration.HasValue && expectedGeneration.Value != _generation))
                return;

            _state = DeviceViewerRuntimeState.WaitingForDevice;
        }

        await MarkWaitingForDeviceAsync(expectedGeneration).ConfigureAwait(false);
        await MarkDeviceIpUnavailableAsync(expectedGeneration).ConfigureAwait(false);
    }

    internal static TimeSpan GetReconnectDelay(DeviceViewerReconnectBackoff reconnectBackoff)
    {
        ArgumentNullException.ThrowIfNull(reconnectBackoff);
        return reconnectBackoff.GetCurrentDelay();
    }

    internal static TimeSpan RegisterReconnectFailure(DeviceViewerReconnectBackoff reconnectBackoff)
    {
        ArgumentNullException.ThrowIfNull(reconnectBackoff);
        return reconnectBackoff.RegisterFailure();
    }

    private async Task MarkTrackerUnavailableAsync(long? expectedGeneration = null)
    {
        if (!CanApplyGeneration(expectedGeneration))
            return;

        await MarkReconnectingAsync(expectedGeneration).ConfigureAwait(false);
    }

    private bool CanApplyGeneration(long? expectedGeneration)
    {
        lock (_gate)
            return !_closed && (!expectedGeneration.HasValue || expectedGeneration.Value == _generation);
    }

    private bool IsCurrentGeneration(long generation)
    {
        return CanApplyGeneration(generation);
    }

    private async Task SetDeviceConnectionStateAsync(
        DeviceConnectionState state,
        long? expectedGeneration = null)
    {
        await InvokeOnDispatcherAsync(
            () => _viewModel.SetDeviceConnectionState(state),
            () => CanApplyGeneration(expectedGeneration)).ConfigureAwait(false);
    }

    private static DeviceConnectionState ToConnectionState(AdbDeviceStatus? status)
    {
        return status switch
        {
            AdbDeviceStatus.Online => DeviceConnectionState.Online,
            AdbDeviceStatus.Unauthorized => DeviceConnectionState.Unauthorized,
            AdbDeviceStatus.Offline => DeviceConnectionState.Offline,
            _ => DeviceConnectionState.Offline
        };
    }

    private AdbDeviceStatus? GetDeviceStatus()
    {
        return _deviceTracker.GetDevice(_serial)?.Status;
    }

    private bool HasOnlineSnapshot()
    {
        return GetDeviceStatus() == AdbDeviceStatus.Online;
    }

    private bool IsTrackerHealthy()
    {
        return _deviceTracker.Health == AdbDeviceTrackerHealth.Connected;
    }

    private bool CanStartStream()
    {
        return IsTrackerHealthy() && HasOnlineSnapshot();
    }

    private void CancelReconnect()
    {
        CancellationTokenSource? reconnectCancellation;
        lock (_gate)
            reconnectCancellation = _reconnectCancellation;
        CancelSafely(reconnectCancellation);
    }

    private void StartStableSessionTimer(IDeviceViewerStreamSession session, long generation)
    {
        CancelStableSessionTimer();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        lock (_gate)
        {
            if (_closed || generation != _generation || !ReferenceEquals(_session, session))
            {
                cancellation.Dispose();
                return;
            }

            _stabilityCancellation = cancellation;
            _stabilityTask = ObserveStableSessionAsync(session, generation, cancellation);
        }
    }

    private async Task ObserveStableSessionAsync(
        IDeviceViewerStreamSession session,
        long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(DeviceViewerReconnectBackoff.StabilityInterval, cancellation.Token)
                .ConfigureAwait(false);

            lock (_gate)
            {
                if (!_closed && generation == _generation &&
                    ReferenceEquals(_session, session) &&
                    _state == DeviceViewerRuntimeState.Streaming)
                {
                    _reconnectBackoff.Reset();
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_stabilityCancellation, cancellation))
                {
                    _stabilityCancellation = null;
                    _stabilityTask = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void CancelStableSessionTimer()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _stabilityCancellation;
            _stabilityCancellation = null;
        }

        CancelSafely(cancellation);
    }

    private async Task StopAndDisposeSessionAsync(IDeviceViewerStreamSession session, string reason)
    {
        try
        {
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to stop stream session for {Serial}. Reason: {Reason}", _serial, reason);
        }
        finally
        {
            try
            {
                session.Dispose();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Failed to dispose stream session for {Serial}. Reason: {Reason}", _serial, reason);
            }
        }
    }

    private Task TrackSessionStopTask(IDeviceViewerStreamSession session, string reason)
    {
        return _sessionStopTasks.Track(StopAndDisposeSessionAsync(session, reason));
    }

    private async Task CloseCoreAsync()
    {
        IDeviceViewerStreamSession? session;
        CancellationTokenSource? startCancellation;
        CancellationTokenSource? reconnectCancellation;
        Task? initialStartTask;
        Task? reconnectTask;
        Task? stabilityTask;

        lock (_gate)
        {
            if (_closed)
                return;

            _closed = true;
            _state = DeviceViewerRuntimeState.Closing;
            ++_generation;
            session = _session;
            _session = null;
            startCancellation = _startAttemptCancellation;
            reconnectCancellation = _reconnectCancellation;
            initialStartTask = _initialStartTask;
            reconnectTask = _reconnectTask;
        }

        _transitionGate.Close();

        _window.ViewerBoundsReady -= OnViewerBoundsReady;
        _window.ViewerBoundsChanged -= OnViewerBoundsChanged;
        _window.ViewerVisibilityChanged -= OnViewerVisibilityChanged;
        _window.Closing -= OnWindowClosing;
        _window.Closed -= OnWindowClosed;
        _deviceTracker.DeviceStateChanged -= OnDeviceStateChanged;
        _deviceTracker.HealthChanged -= OnTrackerHealthChanged;
        _nativeWindowSyncScheduler.Close();
        _deviceIpRefreshLifetime.CancelCurrent();
        CancelStableSessionTimer();
        _lifetimeCancellation.Cancel();
        CancelSafely(startCancellation);
        CancelSafely(reconnectCancellation);
        _viewModel.Dispose();
        session?.Exited -= OnSessionExited;
        session?.SetVisible(false);
        _removeFromRegistry(this);

        if (session != null)
            _ = TrackSessionStopTask(session, "viewer closed");

        await AwaitQuietlyAsync(initialStartTask, "initial start cleanup").ConfigureAwait(false);
        await AwaitQuietlyAsync(reconnectTask, "reconnect cleanup").ConfigureAwait(false);

        // Drain transitions that were already dispatched, including callbacks
        // captured immediately before event unsubscription.
        await _transitionGate.DrainAsync().ConfigureAwait(false);

        Task? deviceIpTask;
        Task[] sessionStopTasks;
        lock (_gate)
        {
            deviceIpTask = _deviceIpTask;
            stabilityTask = _stabilityTask;
            sessionStopTasks = _sessionStopTasks.Snapshot();
        }
        await AwaitQuietlyAsync(deviceIpTask, "device IP cleanup").ConfigureAwait(false);
        await AwaitQuietlyAsync(stabilityTask, "stable stream cleanup").ConfigureAwait(false);
        foreach (var sessionStopTask in sessionStopTasks)
            await AwaitQuietlyAsync(sessionStopTask, "stream session cleanup").ConfigureAwait(false);

        try
        {
            await _startGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            _startGate.Release();
        }
        catch (ObjectDisposedException)
        {
        }

        lock (_gate)
            _state = DeviceViewerRuntimeState.Closed;
        _deviceIpRefreshLifetime.Dispose();
        _lifetimeCancellation.Dispose();
        _startGate.Dispose();
        _ownedScope.Dispose();
    }

    private void OnViewerBoundsReady(object? sender, EventArgs eventArgs)
    {
        _window.LastStartTask = StartAsync();
    }

    private void OnViewerBoundsChanged(object? sender, EventArgs eventArgs)
    {
        RequestNativeWindowSync();
    }

    private void OnViewerVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        RequestNativeWindowSync();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        GetSession()?.SetVisible(false);
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        _ = ObserveBackgroundTaskAsync(CloseAsync(), "window-close cleanup");
    }

    private void RequestNativeWindowSync()
    {
        _ = ObserveBackgroundTaskAsync(
            _nativeWindowSyncScheduler.RequestAsync(),
            "native-window synchronization");
    }

    private async Task SynchronizeNativeWindowOnDispatcherAsync()
    {
        if (_window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished)
            return;

        await _window.Dispatcher
            .InvokeAsync(SynchronizeNativeWindow, DispatcherPriority.Render)
            .Task
            .ConfigureAwait(false);
    }

    private void SynchronizeNativeWindow()
    {
        if (IsClosed())
            return;

        var session = GetSession();
        if (session == null)
            return;

        if (_viewModel.IsStreaming && _window.TryGetViewerBounds(out var bounds))
        {
            session.UpdateBounds(bounds);
            session.SetVisible(true);
        }
        else
        {
            session.SetVisible(false);
        }
    }

    private Task<DeviceViewerStreamBounds> PrepareStreamBoundsAsync(double aspectRatio, long generation)
    {
        return _window.Dispatcher.InvokeAsync(() =>
        {
            if (!CanApplyGeneration(generation))
                throw new OperationCanceledException();

            _viewModel.DeviceAspectRatio = aspectRatio;
            _window.UpdateLayout();
            _window.RefreshStreamLayout();
            _window.UpdateLayout();

            if (!_window.TryGetViewerBounds(out var bounds))
                throw new InvalidOperationException("Device viewer placeholder bounds are not available.");

            return bounds;
        }).Task;
    }

    private Task<IntPtr> GetOwnerHandleAsync()
    {
        return _window.Dispatcher.InvokeAsync(() => _window.NativeOwnerHandle).Task;
    }

    private Task MarkStartingAsync(long generation) =>
        InvokeOnDispatcherAsync(_viewModel.MarkStarting, () => CanApplyGeneration(generation));

    private Task MarkStreamingAsync(long generation) =>
        InvokeOnDispatcherAsync(_viewModel.MarkStreaming, () => CanApplyGeneration(generation));

    private Task MarkStreamErrorAsync(long? generation = null) =>
        InvokeOnDispatcherAsync(_viewModel.MarkStreamError, () => CanApplyGeneration(generation));

    private Task MarkReconnectingAsync(long? generation = null) =>
        InvokeOnDispatcherAsync(_viewModel.MarkReconnecting, () => CanApplyGeneration(generation));

    private Task MarkWaitingForDeviceAsync(long? generation = null) =>
        InvokeOnDispatcherAsync(_viewModel.MarkWaitingForDevice, () => CanApplyGeneration(generation));

    private Task MarkDeviceIpUnavailableAsync(long? generation = null) =>
        InvokeOnDispatcherAsync(_viewModel.MarkDeviceIpUnavailable, () => CanApplyGeneration(generation));

    private async Task RefreshDeviceIpAsync(bool showCheckingState, long sessionGeneration)
    {
        var operation = _deviceIpRefreshLifetime.Start(sessionGeneration);
        if (operation == null)
            return;

        try
        {
            await _window.Dispatcher
                .InvokeAsync(() => IsClosed() || !operation.IsCurrent(sessionGeneration)
                    ? Task.CompletedTask
                    : _viewModel.RefreshDeviceIpAsync(
                        operation.Token,
                        showCheckingState,
                        () => operation.IsCurrent(sessionGeneration)))
                .Task
                .Unwrap()
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Device viewer IP refresh was canceled for {Serial}.", _serial);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Device viewer IP refresh failed for {Serial}.", _serial);
        }
        finally
        {
            operation.Dispose();
        }
    }

    private Task InvokeOnDispatcherAsync(Action action, Func<bool>? canApply = null)
    {
        if (_window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished)
            return Task.CompletedTask;

        return _window.Dispatcher.InvokeAsync(() =>
        {
            if (!IsClosed() && (canApply?.Invoke() ?? true))
                action();
        }).Task;
    }

    private IDeviceViewerStreamSession? GetSession()
    {
        lock (_gate)
            return _session;
    }

    private bool IsStreaming()
    {
        lock (_gate)
            return _state == DeviceViewerRuntimeState.Streaming && _session != null;
    }

    private bool IsClosed()
    {
        lock (_gate)
            return _closed;
    }

    private void CancelStartAttempt()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
            cancellation = _startAttemptCancellation;
        CancelSafely(cancellation);
    }

    private static void CancelSafely(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task AwaitQuietlyAsync(Task? task, string operation)
    {
        if (task == null)
            return;

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Device viewer {Operation} failed for {Serial}.", operation, _serial);
        }
    }

    private async Task ObserveBackgroundTaskAsync(Task task, string operation)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Device viewer {Operation} was canceled for {Serial}.", operation, _serial);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Device viewer {Operation} failed for {Serial}.", operation, _serial);
        }
    }

    public ValueTask DisposeAsync()
    {
        return new(CloseAsync());
    }
}
