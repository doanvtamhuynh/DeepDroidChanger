using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;
using Microsoft.Extensions.Logging;
using ScrcpyNet;

namespace DeepDroidChanger.ViewModels;

public sealed class ViewMultipleDeviceItemViewModel : ObservableObject, IAsyncDisposable
{
    internal static readonly TimeSpan InputReleaseFlushTimeout =
        TimeSpan.FromMilliseconds(500);

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
        DedicatedView,
        Running,
        Idle,
        Failed,
        Busy
    }

    private readonly ISingleViewDeviceSessionFactory _sessionFactory;
    private readonly IAdbDeviceTrackerService _deviceTracker;
    private readonly IViewDeviceClipboardService _clipboardService;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcherService _uiDispatcher;
    private readonly ILogger<ViewMultipleDeviceItemViewModel> _logger;
    private readonly Action<ViewMultipleDeviceItemViewModel, int>? _retryRequest;
    private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;
    private readonly IViewDevicePresentationCoordinator _presentationCoordinator;
    private readonly Func<ViewMultipleDeviceItemViewModel, CancellationToken, Task>? _openViewDeviceRequest;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly object _sessionStateGate = new();
    private readonly object _retryGate = new();

    private ISingleViewDeviceSession? _session;
    private Scrcpy? _scrcpyClient;
    private ViewDeviceSessionState _state = ViewDeviceSessionState.Created;
    private DisplayState _displayState = DisplayState.Connecting;
    private string _deviceName = string.Empty;
    private string _statusText;
    private int _contentWidth;
    private int _contentHeight;
    private double _tileWidth = 220;
    private CancellationTokenSource? _retryCancellation;
    private int _retryAttempt;
    private int _disposed;

    public ViewMultipleDeviceItemViewModel(
        string serial,
        string? deviceName,
        ISingleViewDeviceSessionFactory sessionFactory,
        IAdbDeviceTrackerService deviceTracker,
        IViewDeviceClipboardService clipboardService,
        ILocalizationService localization,
        IUiDispatcherService uiDispatcher,
        ILogger<ViewMultipleDeviceItemViewModel> logger,
        IViewDevicePresentationCoordinator presentationCoordinator,
        Action<ViewMultipleDeviceItemViewModel, int>? retryRequest = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null,
        Func<ViewMultipleDeviceItemViewModel, CancellationToken, Task>? openViewDeviceRequest = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(deviceTracker);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(presentationCoordinator);

        Serial = serial.Trim();
        _deviceName = string.IsNullOrWhiteSpace(deviceName) ? Serial : deviceName.Trim();
        _sessionFactory = sessionFactory;
        _deviceTracker = deviceTracker;
        _clipboardService = clipboardService;
        _localization = localization;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
        _retryRequest = retryRequest;
        _retryDelay = retryDelay ?? Task.Delay;
        _presentationCoordinator = presentationCoordinator;
        _openViewDeviceRequest = openViewDeviceRequest;
        _statusText = _localization.GetString("ViewMultipleDevices_Connecting");
        _localization.LanguageChanged += OnLanguageChanged;

        PasteHostClipboardCommand = new AsyncRelayCommand(
            PasteHostClipboardAsync,
            CanInteract);
        OpenViewDeviceCommand = new AsyncRelayCommand(
            OpenViewDeviceAsync,
            CanOpenViewDevice);
    }

    public string Serial { get; }

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    internal void UpdateDeviceName(string name)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        string normalizedName = string.IsNullOrWhiteSpace(name) ? Serial : name.Trim();
        DeviceName = normalizedName;
    }

    public ViewDeviceSessionState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
                return;

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsDedicatedView));
            NotifyCommandStateChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public Scrcpy? ScrcpyClient
    {
        get => _scrcpyClient;
        private set
        {
            if (!SetProperty(ref _scrcpyClient, value))
                return;

            NotifyCommandStateChanged();
        }
    }

    public int ContentWidth => _contentWidth;

    public int ContentHeight => _contentHeight;

    public double DeviceAspectRatio => ContentWidth > 0 && ContentHeight > 0
        ? (double)ContentWidth / ContentHeight
        : FallbackDeviceAspectRatio;

    public double TileWidth
    {
        get => _tileWidth;
        private set => SetProperty(ref _tileWidth, value);
    }

    public bool IsRunning => _displayState == DisplayState.Running &&
                             State == ViewDeviceSessionState.Running &&
                             ScrcpyClient is not null;

    public bool IsDedicatedView => _displayState == DisplayState.DedicatedView;

    public bool IsFailed => _displayState == DisplayState.Failed;

    public bool IsBusy => _displayState == DisplayState.Busy;

    public IAsyncRelayCommand PasteHostClipboardCommand { get; }

    public IAsyncRelayCommand OpenViewDeviceCommand { get; }

    internal int RetryAttempt => Volatile.Read(ref _retryAttempt);

    public void SetTileSize(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
            return;

        TileWidth = width;
    }

    internal Task RefreshLocalizedTextAsync()
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _disposed) == 0)
                RefreshStatusTextCore();
        });
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ISingleViewDeviceSession? session = null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (IsDedicatedViewActive())
            {
                ResetRetryState();
                await StopCurrentSessionCoreAsync().ConfigureAwait(false);
                await SetDisplayStateAsync(DisplayState.DedicatedView, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            if (IsRunning && IsCurrentSessionRunning())
                return;

            if (!IsTrackerOnline())
            {
                ResetRetryState();
                await StopCurrentSessionCoreAsync().ConfigureAwait(false);
                await SetDisplayStateAsync(DisplayState.Idle, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            await SetDisplayStateAsync(DisplayState.Connecting, cancellationToken)
                .ConfigureAwait(false);
            await StopCurrentSessionCoreAsync().ConfigureAwait(false);

            session = _sessionFactory.Create(ViewDeviceLaunchOptions.ForMultiView(Serial));
            SetCurrentSession(session);
            SubscribeToSession(session);

            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsReadyToPublish(session))
            {
                throw new InvalidOperationException(
                    "The Multiple View session did not reach a valid first-frame state.");
            }

            await PublishRunningSessionAsync(session, cancellationToken)
                .ConfigureAwait(false);
            ResetRetryState();
        }
        catch (OperationCanceledException)
        {
            await StopCurrentSessionCoreAsync(session).ConfigureAwait(false);
            await SetDisplayStateAsync(DisplayState.Idle, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (ScrcpyNetDeviceBusyException exception)
        {
            _logger.LogInformation(
                exception,
                "Multiple View could not acquire the ScrcpyNet lease for {Serial}.",
                Serial);
            await StopCurrentSessionCoreAsync(session).ConfigureAwait(false);
            if (IsDedicatedViewActive())
            {
                await SetDisplayStateAsync(DisplayState.DedicatedView, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                await SetDisplayStateAsync(DisplayState.Busy, CancellationToken.None)
                    .ConfigureAwait(false);
                ScheduleRetryIfEligible();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "ScrcpyNet startup failed for multi-view device {Serial}. Diagnostics: {Diagnostics}",
                Serial,
                session is null ? string.Empty : string.Join(" | ", session.RecentDiagnostics));
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
            await SetDisplayStateAsync(DisplayState.Idle, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    internal async Task SuspendForDedicatedViewAsync(CancellationToken cancellationToken)
    {
        ResetRetryState();
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            await StopCurrentSessionCoreAsync().ConfigureAwait(false);
            await SetDisplayStateAsync(DisplayState.DedicatedView, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
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
            await SetDisplayStateAsync(DisplayState.Idle, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
            _sessionGate.Dispose();
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

    private bool IsTrackerOnline()
    {
        return _deviceTracker.Health == AdbDeviceTrackerHealth.Connected &&
               _deviceTracker.GetDevice(Serial)?.Status == AdbDeviceStatus.Online;
    }

    private bool IsDedicatedViewActive() =>
        _presentationCoordinator.IsDedicatedViewActive(Serial);

    private bool CanInteract()
    {
        return Volatile.Read(ref _disposed) == 0 &&
               IsRunning &&
               ScrcpyClient is not null;
    }

    private void NotifyCommandStateChanged()
    {
        PasteHostClipboardCommand?.NotifyCanExecuteChanged();
        OpenViewDeviceCommand?.NotifyCanExecuteChanged();
    }

    private bool CanOpenViewDevice() =>
        Volatile.Read(ref _disposed) == 0 &&
        _openViewDeviceRequest is not null;

    private Task OpenViewDeviceAsync(CancellationToken cancellationToken)
    {
        return _openViewDeviceRequest is null
            ? Task.CompletedTask
            : _openViewDeviceRequest(this, cancellationToken);
    }

    private void SetCurrentSession(ISingleViewDeviceSession session)
    {
        lock (_sessionStateGate)
            _session = session;
    }

    private bool IsCurrentSession(ISingleViewDeviceSession session)
    {
        lock (_sessionStateGate)
            return ReferenceEquals(_session, session);
    }

    private bool IsCurrentSessionRunning()
    {
        lock (_sessionStateGate)
            return _session?.State == SingleViewDeviceSessionState.Running &&
                   _session.Client is not null;
    }

    private ISingleViewDeviceSession? TakeCurrentSession(
        ISingleViewDeviceSession? expectedSession = null)
    {
        lock (_sessionStateGate)
        {
            if (expectedSession is not null && !ReferenceEquals(_session, expectedSession))
                return null;

            ISingleViewDeviceSession? session = _session;
            _session = null;
            return session;
        }
    }

    private async Task StopCurrentSessionCoreAsync(
        ISingleViewDeviceSession? expectedSession = null)
    {
        ISingleViewDeviceSession? session = TakeCurrentSession(expectedSession);
        if (session is null && expectedSession is not null)
            return;

        if (session is null)
        {
            await ClearScrcpyClientAsync(null).ConfigureAwait(false);
            await SetContentSizeAsync(0, 0, CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }

        UnsubscribeFromSession(session);
        await ClearScrcpyClientAsync(session).ConfigureAwait(false);
        await FlushControlBeforeStopAsync(session).ConfigureAwait(false);

        try
        {
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to stop multi-view scrcpy session for {Serial}.",
                Serial);
        }
        finally
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to dispose multi-view scrcpy session for {Serial}.",
                    Serial);
            }
        }

        await SetContentSizeAsync(0, 0, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task FlushControlBeforeStopAsync(ISingleViewDeviceSession session)
    {
        using CancellationTokenSource timeout = new();
        timeout.CancelAfter(InputReleaseFlushTimeout);
        try
        {
            await session.FlushControlAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Timed out flushing multi-view input recovery for {Serial}.",
                Serial);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Multi-view input recovery flush was unavailable for {Serial}.",
                Serial);
        }
    }

    private void SubscribeToSession(ISingleViewDeviceSession session)
    {
        session.StateChanged += OnSessionStateChanged;
        session.ContentSizeChanged += OnSessionContentSizeChanged;
        session.Exited += OnSessionExited;
    }

    private void UnsubscribeFromSession(ISingleViewDeviceSession session)
    {
        session.StateChanged -= OnSessionStateChanged;
        session.ContentSizeChanged -= OnSessionContentSizeChanged;
        session.Exited -= OnSessionExited;
    }

    private void OnSessionStateChanged(
        object? sender,
        SingleViewDeviceSessionStateChangedEventArgs eventArgs)
    {
        if (sender is ISingleViewDeviceSession session && IsCurrentSession(session))
            _ = HandleSessionStateChangedSafelyAsync(session, eventArgs.Current);
    }

    private void OnSessionContentSizeChanged(
        object? sender,
        SingleViewDeviceContentSizeChangedEventArgs eventArgs)
    {
        if (sender is ISingleViewDeviceSession session && IsCurrentSession(session))
        {
            _ = HandleSessionContentSizeChangedSafelyAsync(
                session,
                eventArgs.Width,
                eventArgs.Height);
        }
    }

    private void OnSessionExited(object? sender, EventArgs eventArgs)
    {
        if (sender is ISingleViewDeviceSession session && IsCurrentSession(session))
            _ = HandleSessionExitedSafelyAsync(session);
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) == 0)
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

    private async Task HandleSessionStateChangedSafelyAsync(
        ISingleViewDeviceSession session,
        SingleViewDeviceSessionState state)
    {
        try
        {
            await SetSessionStateAsync(session, state, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Could not update the multi-view session state for {Serial}.",
                Serial);
        }
    }

    private async Task HandleSessionContentSizeChangedSafelyAsync(
        ISingleViewDeviceSession session,
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

    private async Task HandleSessionExitedSafelyAsync(ISingleViewDeviceSession session)
    {
        try
        {
            await _sessionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    !IsCurrentSession(session))
                {
                    return;
                }

                await SetDisplayStateAsync(DisplayState.Failed, CancellationToken.None)
                    .ConfigureAwait(false);
                await StopCurrentSessionCoreAsync(session).ConfigureAwait(false);
                ScheduleRetryIfEligible();
            }
            finally
            {
                _sessionGate.Release();
            }
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

    private bool IsReadyToPublish(ISingleViewDeviceSession session)
    {
        lock (_sessionStateGate)
        {
            return ReferenceEquals(_session, session) &&
                   Volatile.Read(ref _disposed) == 0 &&
                   session.State == SingleViewDeviceSessionState.Running &&
                   session.Client is not null &&
                   session.ContentWidth > 0 &&
                   session.ContentHeight > 0;
        }
    }

    private Task PublishRunningSessionAsync(
        ISingleViewDeviceSession session,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (!IsReadyToPublish(session))
            {
                throw new InvalidOperationException(
                    "The Multiple View session was no longer current before publication.");
            }

            ScrcpyClient = session.Client;
            SetContentSizeCore(session.ContentWidth, session.ContentHeight);
            SetDisplayStateCore(DisplayState.Running);
        }, cancellationToken);
    }

    private Task SetSessionStateAsync(
        ISingleViewDeviceSession session,
        SingleViewDeviceSessionState state,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || !IsCurrentSession(session))
                return;

            DisplayState displayState = state switch
            {
                SingleViewDeviceSessionState.Failed => DisplayState.Failed,
                SingleViewDeviceSessionState.Closing or
                    SingleViewDeviceSessionState.Closed => DisplayState.Idle,
                SingleViewDeviceSessionState.Running when IsReadyToPublish(session)
                    => DisplayState.Running,
                _ => DisplayState.Connecting
            };
            SetDisplayStateCore(displayState);
        }, cancellationToken);
    }

    private Task ClearScrcpyClientAsync(ISingleViewDeviceSession? expectedSession)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (expectedSession is null)
            {
                if (_session is null)
                    ScrcpyClient = null;
                return;
            }

            if (ReferenceEquals(ScrcpyClient, expectedSession.Client))
                ScrcpyClient = null;
        });
    }

    private Task SetContentSizeAsync(
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (_session is null)
                SetContentSizeCore(width, height);
        }, cancellationToken);
    }

    private Task SetContentSizeAsync(
        ISingleViewDeviceSession session,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _disposed) == 0 && IsCurrentSession(session))
                SetContentSizeCore(width, height);
        }, cancellationToken);
    }

    private Task SetDisplayStateAsync(
        DisplayState state,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(
            () => SetDisplayStateCore(state),
            cancellationToken);
    }

    private void SetDisplayStateCore(DisplayState state)
    {
        _displayState = state;
        State = state switch
        {
            DisplayState.Connecting => ViewDeviceSessionState.Starting,
            DisplayState.DedicatedView => ViewDeviceSessionState.Closed,
            DisplayState.Running => ViewDeviceSessionState.Running,
            DisplayState.Idle => ViewDeviceSessionState.Closed,
            DisplayState.Failed or DisplayState.Busy => ViewDeviceSessionState.Failed,
            _ => ViewDeviceSessionState.Failed
        };
        RefreshStatusTextCore();
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsDedicatedView));
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommandStateChanged();
    }

    private void SetContentSizeCore(int width, int height)
    {
        int newWidth = Math.Max(0, width);
        int newHeight = Math.Max(0, height);
        if (_contentWidth == newWidth && _contentHeight == newHeight)
            return;

        bool widthChanged = _contentWidth != newWidth;
        bool heightChanged = _contentHeight != newHeight;
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
            DisplayState.DedicatedView => "ViewMultipleDevices_OpenedInViewDevice",
            DisplayState.Running => "ViewMultipleDevices_Online",
            DisplayState.Idle => "ViewMultipleDevices_Stopped",
            DisplayState.Failed => "ViewMultipleDevices_Failed",
            DisplayState.Busy => "ViewMultipleDevices_Busy",
            _ => "ViewMultipleDevices_Failed"
        });
    }

    private async Task PasteHostClipboardAsync(CancellationToken cancellationToken)
    {
        ISingleViewDeviceSession? session;
        lock (_sessionStateGate)
            session = _session;

        if (session is null ||
            !IsRunning ||
            session.State != SingleViewDeviceSessionState.Running ||
            session.Client is null)
        {
            return;
        }

        try
        {
            string? text = await _clipboardService
                .GetTextAsync(cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(text))
                return;

            lock (_sessionStateGate)
            {
                if (!ReferenceEquals(_session, session) ||
                    !IsRunning ||
                    session.State != SingleViewDeviceSessionState.Running ||
                    session.Client is null)
                {
                    return;
                }
            }

            await session.PasteHostClipboardAsync(text, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Multiple View clipboard paste failed for {Serial}.",
                Serial);
        }
    }

    private void ScheduleRetryIfEligible()
    {
        if (_retryRequest is null ||
            Volatile.Read(ref _disposed) != 0 ||
            !IsTrackerOnline() ||
            IsDedicatedViewActive())
        {
            return;
        }

        CancellationTokenSource retryCancellation = new();
        CancellationTokenSource? previousRetry;
        int attempt;
        lock (_retryGate)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                !IsTrackerOnline() ||
                Volatile.Read(ref _retryAttempt) >= RetryDelays.Length)
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
            await _retryDelay(
                    RetryDelays[attempt - 1],
                    retryCancellation.Token)
                .ConfigureAwait(false);

            if (!retryCancellation.IsCancellationRequested &&
                Volatile.Read(ref _disposed) == 0 &&
                IsTrackerOnline() &&
                !IsDedicatedViewActive())
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
}
