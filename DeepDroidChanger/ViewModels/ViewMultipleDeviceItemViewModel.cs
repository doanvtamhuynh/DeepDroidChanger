using CommunityToolkit.Mvvm.ComponentModel;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels;

public sealed class ViewMultipleDeviceItemViewModel : ObservableObject, IAsyncDisposable
{
    private const double FallbackDeviceAspectRatio = 9d / 16d;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    private enum DisplayState
    {
        Connecting,
        Online,
        Disconnected,
        Failed
    }

    private readonly IViewDeviceSessionFactory _sessionFactory;
    private readonly IAdbDeviceTrackerService _deviceTracker;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcherService _uiDispatcher;
    private readonly ILogger<ViewMultipleDeviceItemViewModel> _logger;
    private readonly Action<ViewMultipleDeviceItemViewModel, int>? _retryRequest;
    private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly object _sessionStateGate = new();
    private readonly object _retryGate = new();

    private IViewDeviceSession? _session;
    private ViewDeviceSessionState _state = ViewDeviceSessionState.Created;
    private DisplayState _displayState = DisplayState.Connecting;
    private string _statusText;
    private IntPtr _nativeWindowHandle;
    private int _contentWidth;
    private int _contentHeight;
    private double _tileWidth = 220;
    private CancellationTokenSource? _retryCancellation;
    private int _retryAttempt;
    private int _disposed;

    public ViewMultipleDeviceItemViewModel(
        string serial,
        string? deviceName,
        IViewDeviceSessionFactory sessionFactory,
        IAdbDeviceTrackerService deviceTracker,
        ILocalizationService localization,
        IUiDispatcherService uiDispatcher,
        ILogger<ViewMultipleDeviceItemViewModel> logger,
        Action<ViewMultipleDeviceItemViewModel, int>? retryRequest = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(deviceTracker);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        Serial = serial.Trim();
        DeviceName = string.IsNullOrWhiteSpace(deviceName) ? Serial : deviceName.Trim();
        _sessionFactory = sessionFactory;
        _deviceTracker = deviceTracker;
        _localization = localization;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
        _retryRequest = retryRequest;
        _retryDelay = retryDelay ?? Task.Delay;
        _statusText = _localization.GetString("ViewMultipleDevices_Connecting");
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler? NativeWindowHandleChanged;

    public string Serial { get; }

    public string DeviceName { get; }

    public ViewDeviceSessionState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public IntPtr NativeWindowHandle
    {
        get => _nativeWindowHandle;
        private set
        {
            if (SetProperty(ref _nativeWindowHandle, value))
                NativeWindowHandleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int ContentWidth
    {
        get => _contentWidth;
    }

    public int ContentHeight
    {
        get => _contentHeight;
    }

    public double DeviceAspectRatio => ContentWidth > 0 && ContentHeight > 0
        ? (double)ContentWidth / ContentHeight
        : FallbackDeviceAspectRatio;

    public double TileWidth
    {
        get => _tileWidth;
        private set => SetProperty(ref _tileWidth, value);
    }

    public bool IsRunning => _displayState == DisplayState.Online;

    public bool IsDisconnected => _displayState == DisplayState.Disconnected;

    public bool IsFailed => _displayState == DisplayState.Failed;

    public void SetTileSize(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
            return;

        TileWidth = width;
    }

    internal bool IsCurrentNativeWindowHandle(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            Volatile.Read(ref _disposed) != 0 ||
            !IsRunning)
            return false;

        lock (_sessionStateGate)
        {
            return NativeWindowHandle == windowHandle &&
                   _session is not null &&
                   _session.NativeWindowHandle == windowHandle &&
                   _session.State == ViewDeviceSessionState.Running;
        }
    }

    internal void NotifyNativeHostAttached(IntPtr windowHandle)
    {
        if (!IsCurrentNativeWindowHandle(windowHandle))
            return;

        ResetRetryState();
    }

    internal int RetryAttempt => Volatile.Read(ref _retryAttempt);

    internal Task RefreshLocalizedTextAsync()
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            RefreshStatusTextCore();
        });
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IViewDeviceSession? session = null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (_session?.State == ViewDeviceSessionState.Running)
            {
                return;
            }

            if (!IsTrackerOnline())
            {
                ResetRetryState();
                await SetDisplayStateAsync(DisplayState.Disconnected, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            await SetDisplayStateAsync(DisplayState.Connecting, cancellationToken)
                .ConfigureAwait(false);
            await StopCurrentSessionCoreAsync().ConfigureAwait(false);

            session = _sessionFactory.Create(ViewDeviceLaunchOptions.ForMultiView(Serial));
            SetCurrentSession(session);
            SubscribeToSession(session);

            try
            {
                await session.StartAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (Volatile.Read(ref _disposed) != 0 || !IsCurrentSession(session))
                {
                    await StopCurrentSessionCoreAsync(session).ConfigureAwait(false);
                    return;
                }

                await SetContentSizeAsync(
                        session,
                        session.ContentWidth,
                        session.ContentHeight,
                        cancellationToken)
                    .ConfigureAwait(false);
                await SetNativeWindowAsync(session, session.NativeWindowHandle, cancellationToken)
                    .ConfigureAwait(false);
                await SetSessionStateAsync(
                        session,
                        ViewDeviceSessionState.Running,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await StopCurrentSessionCoreAsync(session).ConfigureAwait(false);
                await SetDisplayStateAsync(DisplayState.Disconnected, CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Official scrcpy startup failed for multi-view device {Serial}. Diagnostics: {Diagnostics}",
                    Serial,
                    string.Join(" | ", session.RecentDiagnostics));
                await StopCurrentSessionCoreAsync(session).ConfigureAwait(false);
                await SetDisplayStateAsync(DisplayState.Failed, CancellationToken.None)
                    .ConfigureAwait(false);
                ScheduleRetryIfEligible();
            }
        }
        catch (OperationCanceledException)
        {
            if (session is not null)
                await StopCurrentSessionCoreAsync(session).ConfigureAwait(false);
            await SetDisplayStateAsync(DisplayState.Disconnected, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to start multi-view device {Serial}.", Serial);
            if (session is not null)
                await StopCurrentSessionCoreAsync(session).ConfigureAwait(false);
            await SetDisplayStateAsync(DisplayState.Failed, CancellationToken.None)
                .ConfigureAwait(false);
            ScheduleRetryIfEligible();
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task StopSessionAsync()
    {
        ResetRetryState();
        await _sessionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopCurrentSessionCoreAsync().ConfigureAwait(false);
            await SetDisplayStateAsync(DisplayState.Disconnected, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task MarkDisconnectedAsync()
    {
        ResetRetryState();
        await _sessionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            try
            {
                await SetDisplayStateAsync(DisplayState.Disconnected, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await StopCurrentSessionCoreAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    internal async Task HandleNativeHostFailureAsync(
        Exception exception,
        IntPtr expectedWindowHandle)
    {
        if (expectedWindowHandle == IntPtr.Zero ||
            Volatile.Read(ref _disposed) != 0)
            return;

        _logger.LogWarning(exception, "Failed to embed the scrcpy window for multi-view device {Serial}.", Serial);
        bool retry = false;
        try
        {
            await _sessionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    !IsCurrentNativeWindowHandle(expectedWindowHandle))
                    return;

                CancelPendingRetry();
                try
                {
                    await SetDisplayStateAsync(DisplayState.Failed, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                finally
                {
                    await StopCurrentSessionCoreAsync().ConfigureAwait(false);
                }

                retry = true;
            }
            finally
            {
                _sessionGate.Release();
            }
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
        }
        catch (Exception cleanupException)
        {
            _logger.LogDebug(
                cleanupException,
                "Could not clean up the failed multi-view host for {Serial}.",
                Serial);
        }

        if (retry)
            ScheduleRetryIfEligible();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        ResetRetryState();
        _localization.LanguageChanged -= OnLanguageChanged;
        await _sessionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopCurrentSessionCoreAsync().ConfigureAwait(false);
            await SetDisplayStateAsync(DisplayState.Disconnected, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
            _sessionGate.Dispose();
        }
    }

    private bool IsTrackerOnline()
    {
        return _deviceTracker.Health == AdbDeviceTrackerHealth.Connected &&
               _deviceTracker.GetDevice(Serial)?.Status == AdbDeviceStatus.Online;
    }

    private void SetCurrentSession(IViewDeviceSession session)
    {
        lock (_sessionStateGate)
            _session = session;
    }

    private bool IsCurrentSession(IViewDeviceSession session)
    {
        lock (_sessionStateGate)
            return ReferenceEquals(_session, session);
    }

    private IViewDeviceSession? TakeCurrentSession(IViewDeviceSession? expectedSession = null)
    {
        lock (_sessionStateGate)
        {
            if (expectedSession is not null && !ReferenceEquals(_session, expectedSession))
                return null;

            IViewDeviceSession? session = _session;
            _session = null;
            return session;
        }
    }

    private async Task StopCurrentSessionCoreAsync(IViewDeviceSession? expectedSession = null)
    {
        IViewDeviceSession? session = TakeCurrentSession(expectedSession);
        if (session is null && expectedSession is not null)
            return;

        try
        {
            await SetNativeWindowAsync(IntPtr.Zero, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not clear the embedded scrcpy window for multi-view device {Serial}.",
                Serial);
        }

        try
        {
            await SetContentSizeAsync(0, 0, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not clear the content size for multi-view device {Serial}.",
                Serial);
        }

        if (session is null)
            return;

        UnsubscribeFromSession(session);
        try
        {
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to stop multi-view scrcpy session for {Serial}.", Serial);
        }
        finally
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to dispose multi-view scrcpy session for {Serial}.", Serial);
            }
        }
    }

    private void SubscribeToSession(IViewDeviceSession session)
    {
        session.StateChanged += OnSessionStateChanged;
        session.NativeWindowReady += OnSessionNativeWindowReady;
        session.ContentSizeChanged += OnSessionContentSizeChanged;
        session.Exited += OnSessionExited;
    }

    private void UnsubscribeFromSession(IViewDeviceSession session)
    {
        session.StateChanged -= OnSessionStateChanged;
        session.NativeWindowReady -= OnSessionNativeWindowReady;
        session.ContentSizeChanged -= OnSessionContentSizeChanged;
        session.Exited -= OnSessionExited;
    }

    private void OnSessionStateChanged(
        object? sender,
        ViewDeviceSessionStateChangedEventArgs eventArgs)
    {
        if (sender is IViewDeviceSession session && IsCurrentSession(session))
            _ = HandleSessionStateChangedAsync(session, eventArgs.Current);
    }

    private void OnSessionNativeWindowReady(object? sender, EventArgs eventArgs)
    {
        if (sender is not IViewDeviceSession session || !IsCurrentSession(session))
            return;

        _ = HandleSessionNativeWindowReadyAsync(session);
    }

    private void OnSessionContentSizeChanged(
        object? sender,
        ViewDeviceContentSizeChangedEventArgs eventArgs)
    {
        if (sender is IViewDeviceSession session && IsCurrentSession(session))
        {
            _ = HandleSessionContentSizeChangedSafelyAsync(
                session,
                eventArgs.Width,
                eventArgs.Height);
        }
    }

    private void OnSessionExited(object? sender, EventArgs eventArgs)
    {
        if (sender is IViewDeviceSession session && IsCurrentSession(session))
            _ = HandleSessionExitedSafelyAsync(session);
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _ = RefreshLocalizedTextSafelyAsync();
    }

    private async Task RefreshLocalizedTextSafelyAsync()
    {
        try
        {
            await RefreshLocalizedTextAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not refresh the localized multi-view status for {Serial}.",
                Serial);
        }
    }

    private async Task<bool> HandleSessionExitedAsync(IViewDeviceSession session)
    {
        await _sessionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || !IsCurrentSession(session))
                return false;

            try
            {
                await SetDisplayStateAsync(DisplayState.Failed, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await StopCurrentSessionCoreAsync(session).ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task HandleSessionStateChangedAsync(
        IViewDeviceSession session,
        ViewDeviceSessionState state)
    {
        try
        {
            await SetSessionStateAsync(session, state, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not update the multi-view session state for {Serial}.",
                Serial);
        }
    }

    private async Task HandleSessionNativeWindowReadyAsync(IViewDeviceSession session)
    {
        try
        {
            await SetNativeWindowAsync(session, session.NativeWindowHandle, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not update the embedded window handle for multi-view device {Serial}.",
                Serial);
        }
    }

    private async Task HandleSessionContentSizeChangedSafelyAsync(
        IViewDeviceSession session,
        int width,
        int height)
    {
        try
        {
            await SetContentSizeAsync(session, width, height, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not update the content size for multi-view device {Serial}.",
                Serial);
        }
    }

    private async Task HandleSessionExitedSafelyAsync(IViewDeviceSession session)
    {
        try
        {
            if (await HandleSessionExitedAsync(session).ConfigureAwait(false))
                ScheduleRetryIfEligible();
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not process the exited multi-view session for {Serial}.",
                Serial);
        }
    }

    internal void CancelPendingRetry()
    {
        CancellationTokenSource? retryCancellation;
        lock (_retryGate)
        {
            retryCancellation = _retryCancellation;
            _retryCancellation = null;
        }

        try
        {
            retryCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal void ResetRetryState()
    {
        CancelPendingRetry();
        Interlocked.Exchange(ref _retryAttempt, 0);
    }

    internal void CancelRetry()
    {
        ResetRetryState();
    }

    private void ScheduleRetryIfEligible()
    {
        if (_retryRequest is null ||
            Volatile.Read(ref _disposed) != 0 ||
            !IsTrackerOnline())
        {
            return;
        }

        CancellationTokenSource retryCancellation = new();
        CancellationTokenSource? previousRetry;
        int attempt;
        lock (_retryGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || !IsTrackerOnline())
            {
                retryCancellation.Dispose();
                return;
            }

            if (Volatile.Read(ref _retryAttempt) >= RetryDelays.Length)
            {
                retryCancellation.Dispose();
                return;
            }

            attempt = Interlocked.Increment(ref _retryAttempt);
            previousRetry = _retryCancellation;
            _retryCancellation = retryCancellation;
        }

        try
        {
            previousRetry?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _ = WaitForRetryAsync(retryCancellation, attempt);
    }

    private async Task WaitForRetryAsync(
        CancellationTokenSource retryCancellation,
        int attempt)
    {
        try
        {
            await _retryDelay(RetryDelays[attempt - 1], retryCancellation.Token)
                .ConfigureAwait(false);

            if (!retryCancellation.IsCancellationRequested &&
                Volatile.Read(ref _disposed) == 0 &&
                IsTrackerOnline())
            {
                _retryRequest?.Invoke(this, attempt);
            }
        }
        catch (OperationCanceledException) when (retryCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not schedule the multi-view scrcpy retry for {Serial}.",
                Serial);
        }
        finally
        {
            lock (_retryGate)
            {
                if (ReferenceEquals(_retryCancellation, retryCancellation))
                    _retryCancellation = null;
            }

            retryCancellation.Dispose();
        }
    }

    private Task SetSessionStateAsync(
        IViewDeviceSession session,
        ViewDeviceSessionState state,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || !IsCurrentSession(session))
                return;

            State = state;
            SetDisplayStateCore(ToDisplayState(state));
        }, cancellationToken);
    }

    private Task SetNativeWindowAsync(IntPtr handle, CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() => NativeWindowHandle = handle, cancellationToken);
    }

    private Task SetContentSizeAsync(
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            SetContentSizeCore(width, height);
        }, cancellationToken);
    }

    private Task SetContentSizeAsync(
        IViewDeviceSession session,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || !IsCurrentSession(session))
                return;

            SetContentSizeCore(width, height);
        }, cancellationToken);
    }

    private Task SetNativeWindowAsync(
        IViewDeviceSession session,
        IntPtr handle,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || !IsCurrentSession(session))
                return;

            NativeWindowHandle = handle;
        }, cancellationToken);
    }

    private Task SetDisplayStateAsync(DisplayState state, CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() => SetDisplayStateCore(state), cancellationToken);
    }

    private void SetDisplayStateCore(DisplayState state)
    {
        bool displayStateChanged = _displayState != state;
        _displayState = state;
        State = state switch
        {
            DisplayState.Connecting => ViewDeviceSessionState.Starting,
            DisplayState.Online => ViewDeviceSessionState.Running,
            DisplayState.Disconnected => ViewDeviceSessionState.Closed,
            DisplayState.Failed => ViewDeviceSessionState.Failed,
            _ => ViewDeviceSessionState.Failed
        };
        RefreshStatusTextCore();

        if (!displayStateChanged)
            return;

        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(IsFailed));
    }

    private void SetContentSizeCore(int width, int height)
    {
        int newWidth = Math.Max(0, width);
        int newHeight = Math.Max(0, height);
        bool widthChanged = _contentWidth != newWidth;
        bool heightChanged = _contentHeight != newHeight;

        if (!widthChanged && !heightChanged)
            return;

        _contentWidth = newWidth;
        _contentHeight = newHeight;

        if (widthChanged)
            OnPropertyChanged(nameof(ContentWidth));

        if (heightChanged)
            OnPropertyChanged(nameof(ContentHeight));

        OnPropertyChanged(nameof(DeviceAspectRatio));
    }

    private void RefreshStatusTextCore()
    {
        StatusText = _localization.GetString(_displayState switch
        {
            DisplayState.Connecting => "ViewMultipleDevices_Connecting",
            DisplayState.Online => "ViewMultipleDevices_Online",
            DisplayState.Disconnected => "ViewMultipleDevices_Disconnected",
            DisplayState.Failed => "ViewMultipleDevices_Failed",
            _ => "ViewMultipleDevices_Failed"
        });
    }

    private static DisplayState ToDisplayState(ViewDeviceSessionState state)
    {
        return state switch
        {
            ViewDeviceSessionState.Running => DisplayState.Online,
            ViewDeviceSessionState.Failed => DisplayState.Failed,
            ViewDeviceSessionState.Closed or ViewDeviceSessionState.Closing => DisplayState.Disconnected,
            _ => DisplayState.Connecting
        };
    }
}
