using System.IO;
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

public sealed partial class ViewDeviceViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan DeviceOnlineDebounce = TimeSpan.FromMilliseconds(400);
    internal static readonly TimeSpan PowerCooldown = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan InputReleaseFlushTimeout = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan[] DefaultRestartDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    private readonly ISingleViewDeviceSessionFactory _sessionFactory;
    private readonly IAdbDeviceTrackerService _deviceTracker;
    private readonly IAdbCommandService _adbCommandService;
    private readonly IFilePickerDialogService _filePicker;
    private readonly IViewDeviceScreenshotService _screenshotService;
    private readonly ILocalizationService _localization;
    private readonly IDeviceConfigService _deviceConfigService;
    private readonly IDeviceActionService _deviceActionService;
    private readonly IPackageInstallService _packageInstallService;
    private readonly IPollingService _pollingService;
    private readonly IUiDispatcherService _uiDispatcher;
    private readonly IViewDeviceClipboardService _clipboardService;
    private readonly ILogger<ViewDeviceViewModel> _logger;
    private readonly TimeSpan[] _restartDelays;
    private readonly Func<TimeSpan, CancellationToken, Task> _powerCooldownDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _runtimeVerificationDelay;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly SemaphoreSlim _clipboardSynchronizationGate = new(1, 1);
    private readonly object _evaluationSchedulingGate = new();

    private CancellationTokenSource? _pendingEvaluationCancellation;
    private Task _pendingEvaluationTask = Task.CompletedTask;
    private ISingleViewDeviceSession? _session;
    private ISingleViewDeviceSession? _establishedSession;
    private string _serial = string.Empty;
    private string _deviceName = string.Empty;
    private ViewDeviceSessionState _state = ViewDeviceSessionState.Created;
    private string _statusText = string.Empty;
    private ScrcpyNet.Scrcpy? _scrcpyClient;
    private int _contentWidth;
    private int _contentHeight;
    private bool _hasRun;
    private int _restartAttempt;
    private int _generation;
    private int _manualReconnectActive;
    private int _powerCooldownActive;
    private int _lastRecoveryScheduleGeneration = -1;
    private int _disposed;
    private int _viewRotationQuarterTurns;

    public ViewDeviceViewModel(
        ISingleViewDeviceSessionFactory sessionFactory,
        IAdbDeviceTrackerService deviceTracker,
        IAdbCommandService adbCommandService,
        IFilePickerDialogService filePicker,
        IViewDeviceScreenshotService screenshotService,
        ILocalizationService localization,
        IUiDispatcherService uiDispatcher,
        IViewDeviceClipboardService clipboardService,
        ILogger<ViewDeviceViewModel> logger,
        IDeviceConfigService deviceConfigService,
        IDeviceActionService deviceActionService,
        IPackageInstallService packageInstallService,
        IPollingService pollingService)
        : this(
            sessionFactory,
            deviceTracker,
            adbCommandService,
            filePicker,
            screenshotService,
            localization,
            uiDispatcher,
            clipboardService,
            logger,
            deviceConfigService,
            deviceActionService,
            packageInstallService,
            pollingService,
            DefaultRestartDelays)
    {
    }

    internal ViewDeviceViewModel(
        ISingleViewDeviceSessionFactory sessionFactory,
        IAdbDeviceTrackerService deviceTracker,
        IAdbCommandService adbCommandService,
        IFilePickerDialogService filePicker,
        IViewDeviceScreenshotService screenshotService,
        ILocalizationService localization,
        IUiDispatcherService uiDispatcher,
        IViewDeviceClipboardService clipboardService,
        ILogger<ViewDeviceViewModel> logger,
        IDeviceConfigService deviceConfigService,
        IDeviceActionService deviceActionService,
        IPackageInstallService packageInstallService,
        IPollingService pollingService,
        IReadOnlyList<TimeSpan> restartDelays,
        Func<TimeSpan, CancellationToken, Task>? powerCooldownDelay = null,
        Func<TimeSpan, CancellationToken, Task>? runtimeVerificationDelay = null)
    {
        _sessionFactory = sessionFactory;
        _deviceTracker = deviceTracker;
        _adbCommandService = adbCommandService;
        _filePicker = filePicker;
        _screenshotService = screenshotService;
        _localization = localization;
        _deviceConfigService = deviceConfigService;
        _deviceActionService = deviceActionService;
        _packageInstallService = packageInstallService;
        _pollingService = pollingService;
        _uiDispatcher = uiDispatcher;
        _clipboardService = clipboardService;
        _logger = logger;
        ArgumentNullException.ThrowIfNull(restartDelays);
        _restartDelays = restartDelays.ToArray();
        _powerCooldownDelay = powerCooldownDelay ?? ((delay, cancellationToken) =>
            Task.Delay(delay, cancellationToken));
        _runtimeVerificationDelay = runtimeVerificationDelay ?? ((delay, cancellationToken) =>
            Task.Delay(delay, cancellationToken));

        RetryCommand = new AsyncRelayCommand(RetryAsync, CanRetry);
        ReconnectCommand = new AsyncRelayCommand(ReconnectAsync, CanReconnect);
        BackCommand = new AsyncRelayCommand(SendBackAsync, CanInteract);
        HomeCommand = new AsyncRelayCommand(() => SendKeyAsync(3), CanInteract);
        RecentCommand = new AsyncRelayCommand(() => SendKeyAsync(187), CanInteract);
        PowerCommand = new AsyncRelayCommand(SendPowerAsync, CanSendPower);
        VolumeUpCommand = new AsyncRelayCommand(() => SendKeyAsync(24), CanInteract);
        VolumeDownCommand = new AsyncRelayCommand(() => SendKeyAsync(25), CanInteract);
        ScreenOnCommand = new AsyncRelayCommand(ScreenOnAsync, CanInteract);
        ScreenOffCommand = new AsyncRelayCommand(ScreenOffAsync, CanInteract);
        ScreenshotCommand = new AsyncRelayCommand(SaveScreenshotAsync, CanInteract);
        PasteHostClipboardCommand = new AsyncRelayCommand(PasteHostClipboardAsync, CanInteract);
        PasteHostClipboardWithPasteKeyCommand = new AsyncRelayCommand(
            PasteHostClipboardWithPasteKeyAsync,
            CanInteract);
        InjectHostClipboardCommand = new AsyncRelayCommand(InjectHostClipboardAsync, CanInteract);
        CopyClipboardCommand = new AsyncRelayCommand(() => RequestClipboardAsync(ScrcpyCopyKey.Copy), CanInteract);
        CutClipboardCommand = new AsyncRelayCommand(() => RequestClipboardAsync(ScrcpyCopyKey.Cut), CanInteract);
        ExpandNotificationPanelCommand = new AsyncRelayCommand(ExpandNotificationPanelAsync, CanInteract);
        ExpandSettingsPanelCommand = new AsyncRelayCommand(ExpandSettingsPanelAsync, CanInteract);
        CollapsePanelsCommand = new AsyncRelayCommand(CollapsePanelsAsync, CanInteract);
        MenuCommand = new AsyncRelayCommand(
            () => SendKeyAsync((int)AndroidKeycode.AKEYCODE_MENU),
            CanInteract);
        InitializeToolCommands();
        StatusText = GetStateText(State);
    }

    public string Serial
    {
        get => _serial;
        private set => SetProperty(ref _serial, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    public ViewDeviceSessionState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
                return;

            StatusText = GetStateText(value);
            NotifyAvailabilityPropertiesChanged();
            NotifyCommandStateChanged();
            OnViewDeviceStateChangedForTools(value);
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ScrcpyNet.Scrcpy? ScrcpyClient
    {
        get => _scrcpyClient;
        private set => SetProperty(ref _scrcpyClient, value);
    }

    public int ContentWidth
    {
        get => _contentWidth;
        private set => SetProperty(ref _contentWidth, value);
    }

    public int ContentHeight
    {
        get => _contentHeight;
        private set => SetProperty(ref _contentHeight, value);
    }

    public double DeviceAspectRatio => ContentWidth > 0 && ContentHeight > 0
        ? (double)ContentWidth / ContentHeight
        : 0;

    public int ViewRotationQuarterTurns
    {
        get => _viewRotationQuarterTurns;
        private set
        {
            int normalized = ((value % 4) + 4) % 4;
            if (!SetProperty(ref _viewRotationQuarterTurns, normalized))
                return;

            OnPropertyChanged(nameof(ViewRotationAngle));
            OnPropertyChanged(nameof(ViewAspectRatio));
        }
    }

    public double ViewRotationAngle => ViewRotationQuarterTurns * 90d;

    public double ViewAspectRatio
    {
        get
        {
            double aspect = DeviceAspectRatio;
            if (!double.IsFinite(aspect) || aspect <= 0)
                return aspect;

            return (ViewRotationQuarterTurns & 1) == 0
                ? aspect
                : 1d / aspect;
        }
    }

    internal static int NextViewRotation(int currentQuarterTurns)
    {
        int normalized = ((currentQuarterTurns % 4) + 4) % 4;
        return (normalized + 1) % 4;
    }

    public bool IsRunning => IsInteractiveSessionSnapshot(
        State,
        Volatile.Read(ref _session),
        Volatile.Read(ref _establishedSession),
        Volatile.Read(ref _disposed) != 0);
    public bool IsUnavailable => !IsRunning;

    public IAsyncRelayCommand RetryCommand { get; }
    public IAsyncRelayCommand ReconnectCommand { get; }
    public IAsyncRelayCommand BackCommand { get; }
    public IAsyncRelayCommand HomeCommand { get; }
    public IAsyncRelayCommand RecentCommand { get; }
    public IAsyncRelayCommand PowerCommand { get; }
    public IAsyncRelayCommand VolumeUpCommand { get; }
    public IAsyncRelayCommand VolumeDownCommand { get; }
    public IAsyncRelayCommand ScreenOnCommand { get; }
    public IAsyncRelayCommand ScreenOffCommand { get; }
    public IAsyncRelayCommand ScreenshotCommand { get; }
    public IAsyncRelayCommand PasteHostClipboardCommand { get; }
    public IAsyncRelayCommand PasteHostClipboardWithPasteKeyCommand { get; }
    public IAsyncRelayCommand InjectHostClipboardCommand { get; }
    public IAsyncRelayCommand CopyClipboardCommand { get; }
    public IAsyncRelayCommand CutClipboardCommand { get; }
    public IAsyncRelayCommand ExpandNotificationPanelCommand { get; }
    public IAsyncRelayCommand ExpandSettingsPanelCommand { get; }
    public IAsyncRelayCommand CollapsePanelsCommand { get; }
    public IAsyncRelayCommand MenuCommand { get; }

    internal int CurrentGenerationForTesting => Volatile.Read(ref _generation);
    internal int RestartAttemptForTesting => Volatile.Read(ref _restartAttempt);
    internal bool HasCurrentSessionForTesting => Volatile.Read(ref _session) is not null;
    internal bool HasEstablishedSessionForTesting =>
        Volatile.Read(ref _establishedSession) is not null;

    internal void SetContentSizeForTesting(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException();

        ContentWidth = width;
        ContentHeight = height;
        OnPropertyChanged(nameof(DeviceAspectRatio));
        OnPropertyChanged(nameof(ViewAspectRatio));
        NotifyToolCommandStateChanged();
    }
    internal Task PendingEvaluationForTesting
    {
        get
        {
            lock (_evaluationSchedulingGate)
                return _pendingEvaluationTask;
        }
    }

    public async Task InitializeAsync(
        string serial,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        Serial = serial;
        DeviceName = string.IsNullOrWhiteSpace(displayName) ? serial : displayName;
        State = ViewDeviceSessionState.CheckingDevice;
        _deviceTracker.DeviceStateChanged += OnDeviceStateChanged;
        _deviceTracker.HealthChanged += OnTrackerHealthChanged;
        _localization.LanguageChanged += OnLanguageChanged;

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        try
        {
            await _deviceTracker.StartAsync(linked.Token).ConfigureAwait(true);
            await QueueEvaluationAsync(TimeSpan.Zero, resetRestartAttempt: true).ConfigureAwait(true);
            if (Volatile.Read(ref _disposed) != 0)
                return;
            await RefreshVisibleRuntimeStateAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _deviceTracker.DeviceStateChanged -= OnDeviceStateChanged;
        _deviceTracker.HealthChanged -= OnTrackerHealthChanged;
        await StopRuntimePollingAsync().ConfigureAwait(true);
        _localization.LanguageChanged -= OnLanguageChanged;
        _lifetimeCancellation.Cancel();
        Interlocked.Increment(ref _generation);

        CancellationTokenSource? pending;
        Task pendingTask;
        lock (_evaluationSchedulingGate)
        {
            pending = Interlocked.Exchange(ref _pendingEvaluationCancellation, null);
            pendingTask = _pendingEvaluationTask;
        }
        CancelEvaluation(pending);
        try
        {
            await pendingTask.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }

        await _clipboardSynchronizationGate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
        _clipboardSynchronizationGate.Release();

        await _transitionGate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            State = ViewDeviceSessionState.Closing;
            try
            {
                await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(true);
                State = ViewDeviceSessionState.Closed;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "View Device cleanup failed while disposing {Serial}.", Serial);
                await TryEmergencyDetachScrcpyClientAsync().ConfigureAwait(true);
                State = ViewDeviceSessionState.Failed;
            }
        }
        finally
        {
            _transitionGate.Release();
            _transitionGate.Dispose();
            _clipboardSynchronizationGate.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }

    private Task RetryAsync()
    {
        return QueueEvaluationAsync(TimeSpan.Zero, resetRestartAttempt: true);
    }

    private async Task ReconnectAsync()
    {
        if (Interlocked.CompareExchange(ref _manualReconnectActive, 1, 0) != 0)
            return;

        await _uiDispatcher
            .InvokeAsync(ReconnectCommand.NotifyCanExecuteChanged, CancellationToken.None)
            .ConfigureAwait(false);
        int reconnectGeneration = -1;
        try
        {
            reconnectGeneration = CancelPendingEvaluation(resetRestartAttempt: true);
            await _transitionGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
            try
            {
                ISingleViewDeviceSession? currentSession = GetInteractiveSession();
                if (currentSession is null)
                {
                    return;
                }

                await SetManualReconnectStateForSessionAsync(
                        currentSession,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                await StopCurrentSessionAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
                if (Volatile.Read(ref _disposed) != 0)
                    return;

                await StartSessionAsync(
                        reconnectGeneration,
                        _lifetimeCancellation.Token,
                        requireCurrentGeneration: false)
                    .ConfigureAwait(false);
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Manual View Device reconnect failed for {Serial}.", Serial);
            if (reconnectGeneration >= 0)
            {
                await TrySetRecoveryFailedStateAsync(reconnectGeneration).ConfigureAwait(false);
                ScheduleRestartAfterFailure(reconnectGeneration);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _manualReconnectActive, 0);
            await _uiDispatcher
                .InvokeAsync(ReconnectCommand.NotifyCanExecuteChanged, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private int CancelPendingEvaluation(bool resetRestartAttempt)
    {
        CancellationTokenSource? pending;
        int generation;
        lock (_evaluationSchedulingGate)
        {
            if (resetRestartAttempt)
                Interlocked.Exchange(ref _restartAttempt, 0);

            generation = Interlocked.Increment(ref _generation);
            pending = Interlocked.Exchange(ref _pendingEvaluationCancellation, null);
        }

        CancelEvaluation(pending);
        return generation;
    }

    private Task QueueEvaluationAsync(TimeSpan delay, bool resetRestartAttempt = false)
    {
        lock (_evaluationSchedulingGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return Task.CompletedTask;

            if (resetRestartAttempt)
                Interlocked.Exchange(ref _restartAttempt, 0);

            int generation = Interlocked.Increment(ref _generation);
            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token);
            CancellationTokenSource? previous = Interlocked.Exchange(
                ref _pendingEvaluationCancellation,
                cancellation);
            CancelEvaluation(previous);

            Task task = EvaluateAfterDelayAsync(generation, delay, cancellation);
            _pendingEvaluationTask = task;
            return task;
        }
    }

    private async Task EvaluateAfterDelayAsync(
        int generation,
        TimeSpan delay,
        CancellationTokenSource cancellation)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
            await EvaluateDeviceAsync(generation, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await HandleEvaluationFailureAsync(generation, exception).ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(Volatile.Read(ref _pendingEvaluationCancellation), cancellation))
                Interlocked.CompareExchange(ref _pendingEvaluationCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private async Task HandleEvaluationFailureAsync(
        int generation,
        Exception exception)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            _lifetimeCancellation.IsCancellationRequested ||
            generation != Volatile.Read(ref _generation))
        {
            return;
        }

        _logger.LogWarning(
            exception,
            "Queued View Device evaluation failed for {Serial}.",
            Serial);

        bool scheduleRecovery = false;
        try
        {
            await _transitionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    _lifetimeCancellation.IsCancellationRequested ||
                    generation != Volatile.Read(ref _generation))
                {
                    return;
                }

                try
                {
                    await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "View Device cleanup after evaluation failure failed for {Serial}.",
                        Serial);
                }

                await TryEmergencyDetachScrcpyClientAsync().ConfigureAwait(false);
                await TrySetRecoveryFailedStateAsync(generation).ConfigureAwait(false);
                scheduleRecovery = true;
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        catch (Exception handlingException)
        {
            _logger.LogWarning(
                handlingException,
                "View Device evaluation failure recovery failed for {Serial}.",
                Serial);
            scheduleRecovery = true;
        }

        if (scheduleRecovery &&
            Volatile.Read(ref _disposed) == 0 &&
            !_lifetimeCancellation.IsCancellationRequested &&
            generation == Volatile.Read(ref _generation))
        {
            ScheduleRestartAfterFailure(generation);
        }
    }

    private async Task EvaluateDeviceAsync(int generation, CancellationToken cancellationToken)
    {
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _generation) || Volatile.Read(ref _disposed) != 0)
                return;

            if (_deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
            {
                ISingleViewDeviceSession? healthSession = Volatile.Read(ref _session);
                if (healthSession?.State == SingleViewDeviceSessionState.Running)
                    return;

                if (healthSession is not null)
                {
                    try
                    {
                        await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(
                            exception,
                            "View Device cleanup while the ADB tracker was unavailable failed for {Serial}.",
                            Serial);
                        await TryEmergencyDetachScrcpyClientAsync().ConfigureAwait(false);
                    }
                }

                await SetStateAsync(ViewDeviceSessionState.AdbUnavailable, cancellationToken).ConfigureAwait(false);
                return;
            }

            AdbDevice? device = _deviceTracker.GetDevice(Serial);
            if (device?.Status == AdbDeviceStatus.Unauthorized)
            {
                await SetStateAsync(ViewDeviceSessionState.Unauthorized, cancellationToken).ConfigureAwait(false);
                await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (device?.Status != AdbDeviceStatus.Online)
            {
                await SetStateAsync(ViewDeviceSessionState.WaitingForDevice, cancellationToken).ConfigureAwait(false);
                await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            ISingleViewDeviceSession? onlineSession = Volatile.Read(ref _session);
            if (onlineSession?.State == SingleViewDeviceSessionState.Running)
                return;

            await StartSessionAsync(generation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task StartSessionAsync(
        int generation,
        CancellationToken cancellationToken,
        bool requireCurrentGeneration = true)
    {
        ISingleViewDeviceSession? session = null;
        try
        {
            // Cleanup and state publication stay inside this boundary so a failure
            // from an old session cannot fault a queued tracker evaluation.
            await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            await SetStateAsync(
                    _hasRun ? ViewDeviceSessionState.Reconnecting : ViewDeviceSessionState.Starting,
                    cancellationToken)
                .ConfigureAwait(false);

            CommandResult confirmation = await _adbCommandService
                .RunAdbAsync(Serial, "get-state", cancellationToken)
                .ConfigureAwait(false);
            if (confirmation.ExitCode != 0 ||
                !string.Equals(confirmation.StandardOutput.Trim(), "device", StringComparison.OrdinalIgnoreCase))
            {
                ViewDeviceSessionState state = confirmation.StandardError.Contains(
                    "unauthorized",
                    StringComparison.OrdinalIgnoreCase)
                    ? ViewDeviceSessionState.Unauthorized
                    : ViewDeviceSessionState.WaitingForDevice;
                await SetStateAsync(state, cancellationToken).ConfigureAwait(false);
                if (state == ViewDeviceSessionState.WaitingForDevice)
                    ScheduleRestartAfterFailure(generation);
                return;
            }

            session = _sessionFactory.Create(new ViewDeviceLaunchOptions(Serial));
            Volatile.Write(ref _session, session);
            session.StateChanged += OnSessionStateChanged;
            session.ContentSizeChanged += OnSessionContentSizeChanged;
            session.ClipboardChanged += OnSessionClipboardChanged;
            session.Exited += OnSessionExited;

            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            if (requireCurrentGeneration && generation != Volatile.Read(ref _generation))
            {
                await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }

            if (!TryEstablishSessionForPublication(session, generation, requireCurrentGeneration))
            {
                throw new InvalidOperationException(
                    "The Single View session was no longer running immediately after startup.");
            }

            await PublishRunningSessionAsync(
                    session,
                    generation,
                    requireCurrentGeneration,
                    cancellationToken)
                .ConfigureAwait(false);

            _hasRun = true;
            Interlocked.Exchange(ref _restartAttempt, 0);
        }
        catch (OperationCanceledException)
        {
            await TryCleanupAfterStartFailureAsync(session).ConfigureAwait(false);
            throw;
        }
        catch (ScrcpyNetDeviceBusyException exception)
        {
            _logger.LogInformation(
                exception,
                "ScrcpyNet is already leased by another viewer for {Serial}; automatic recovery is paused.",
                Serial);
            await TryCleanupAfterStartFailureAsync(session).ConfigureAwait(false);
            await TrySetBusyStateAsync(generation).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "ScrcpyNet startup failed for {Serial}. Diagnostics: {Diagnostics}",
                Serial,
                session is null ? string.Empty : string.Join(" | ", session.RecentDiagnostics));
            await TryCleanupAfterStartFailureAsync(session).ConfigureAwait(false);
            await TrySetRecoveryFailedStateAsync(generation).ConfigureAwait(false);
            ScheduleRestartAfterFailure(generation);
        }
    }

    private bool IsReadyToPublish(
        ISingleViewDeviceSession session,
        int generation,
        bool requireCurrentGeneration,
        bool requireEstablished = false)
    {
        ISingleViewDeviceSession? currentSession = Volatile.Read(ref _session);
        return ReferenceEquals(currentSession, session) &&
               Volatile.Read(ref _disposed) == 0 &&
               session.State == SingleViewDeviceSessionState.Running &&
               session.Client is not null &&
               (!requireCurrentGeneration || generation == Volatile.Read(ref _generation)) &&
               (!requireEstablished ||
                ReferenceEquals(session, Volatile.Read(ref _establishedSession)));
    }

    private bool TryEstablishSessionForPublication(
        ISingleViewDeviceSession session,
        int generation,
        bool requireCurrentGeneration)
    {
        ISingleViewDeviceSession? existing = Interlocked.CompareExchange(
            ref _establishedSession,
            session,
            null);
        if (existing is null)
            QueueAvailabilityNotification();

        if (existing is not null && !ReferenceEquals(existing, session))
            return false;

        if (IsReadyToPublish(
                session,
                generation,
                requireCurrentGeneration,
                requireEstablished: true))
        {
            return true;
        }

        ClearEstablishedSession(session);
        return false;
    }

    private Task PublishRunningSessionAsync(
        ISingleViewDeviceSession session,
        int generation,
        bool requireCurrentGeneration,
        CancellationToken cancellationToken)
    {
        ScrcpyNet.Scrcpy expectedClient = session.Client
            ?? throw new InvalidOperationException("The Single View session has no client to publish.");

        return PublishScrcpyClientForSessionAsync(
            session,
            expectedClient,
            generation,
            requireCurrentGeneration,
            cancellationToken);
    }

    private Task PublishScrcpyClientForSessionAsync(
        ISingleViewDeviceSession session,
        ScrcpyNet.Scrcpy expectedClient,
        int generation,
        bool requireCurrentGeneration,
        CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(
            () =>
            {
                // This is the final check immediately before publishing the
                // client, dimensions, and Running state.
                if (!IsReadyToPublish(
                        session,
                        generation,
                        requireCurrentGeneration,
                        requireEstablished: true))
                {
                    throw new InvalidOperationException(
                        "The Single View session exited before it could be published as Running.");
                }

                if (!ReferenceEquals(expectedClient, session.Client) || ScrcpyClient is not null)
                {
                    throw new InvalidOperationException(
                        "The Single View client changed before it could be published.");
                }

                ScrcpyClient = expectedClient;
                if (ContentWidth != session.ContentWidth ||
                    ContentHeight != session.ContentHeight)
                {
                    ContentWidth = session.ContentWidth;
                    ContentHeight = session.ContentHeight;
                    OnPropertyChanged(nameof(DeviceAspectRatio));
                    OnPropertyChanged(nameof(ViewAspectRatio));
                    NotifyToolCommandStateChanged();
                }

                State = ViewDeviceSessionState.Running;
            },
            cancellationToken);
    }

    private async Task TryCleanupAfterStartFailureAsync(ISingleViewDeviceSession? session)
    {
        try
        {
            await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            _logger.LogWarning(
                cleanupException,
                "View Device cleanup after startup failure failed for {Serial}.",
                Serial);
            await TryEmergencyDetachScrcpyClientAsync().ConfigureAwait(false);
        }
    }

    private async Task StopCurrentSessionAsync(CancellationToken cancellationToken)
    {
        ISingleViewDeviceSession? session = Volatile.Read(ref _session);
        if (session is null)
        {
            ISingleViewDeviceSession? established = Volatile.Read(ref _establishedSession);
            if (established is not null)
                ClearEstablishedSession(established);
            return;
        }

        ScrcpyNet.Scrcpy? expectedClient = session.Client;
        Exception? failure = null;
        try
        {
            // Detach the display while the old session is still current. The
            // identity check in the dispatcher action prevents a delayed old
            // callback from clearing a replacement client's binding.
            await DetachScrcpyClientForSessionAsync(
                    session,
                    expectedClient,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "View Device client unbind failed before input recovery for {Serial}.",
                Serial);
            failure = exception;
        }

        Volatile.Write(ref _session, null);
        if (!ClearEstablishedSession(session))
            QueueAvailabilityNotification();
        session.StateChanged -= OnSessionStateChanged;
        session.ContentSizeChanged -= OnSessionContentSizeChanged;
        session.ClipboardChanged -= OnSessionClipboardChanged;
        session.Exited -= OnSessionExited;

        if (failure is not null && expectedClient is not null)
        {
            // The identity-safe detach above is the normal path. If its UI
            // callback failed, the old session is no longer current, so make
            // one bounded best-effort emergency detach before flushing input.
            await TryEmergencyDetachScrcpyClientAsync().ConfigureAwait(false);
        }

        // Flush is deliberately best-effort and bounded. A broken USB/control
        // socket must never make close or reconnect wait indefinitely.
        await FlushControlBeforeStopAsync(session, cancellationToken).ConfigureAwait(false);

        try
        {
            await session.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            failure = exception;
            if (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "View Device fallback stop failed for {Serial}.",
                        Serial);
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
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
                    "View Device session disposal failed for {Serial}.",
                    Serial);
                failure ??= exception;
            }

        }

        if (failure is not null)
            throw failure;
    }

    private async Task FlushControlBeforeStopAsync(
        ISingleViewDeviceSession session,
        CancellationToken lifecycleCancellation)
    {
        using CancellationTokenSource timeout = new(InputReleaseFlushTimeout);
        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                lifecycleCancellation,
                timeout.Token);

        try
        {
            Task flush = session.FlushControlAsync(linkedCancellation.Token);
            await flush.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogDebug(
                exception,
                "View Device input-release flush was canceled or timed out for {Serial}; continuing shutdown.",
                Serial);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "View Device input-release flush was unavailable for {Serial}; continuing shutdown.",
                Serial);
        }
    }

    private void OnDeviceStateChanged(object? sender, AdbDeviceStateChangedEventArgs eventArgs)
    {
        if (!string.Equals(eventArgs.Serial, Serial, StringComparison.OrdinalIgnoreCase))
            return;

        if (IsDeviceOnline)
            QueueRuntimeStateRefresh();
        else
            QueueRuntimeStateClear();

        NotifyToolAvailabilityChanged();
        TimeSpan delay = eventArgs.Current?.Status == AdbDeviceStatus.Online
            ? DeviceOnlineDebounce
            : TimeSpan.Zero;
        QueueEvaluationSafely(delay, resetRestartAttempt: eventArgs.Current?.Status == AdbDeviceStatus.Online);
    }

    private void OnTrackerHealthChanged(object? sender, AdbDeviceTrackerHealthChangedEventArgs eventArgs)
    {
        if (IsDeviceOnline)
            QueueRuntimeStateRefresh();
        else
            QueueRuntimeStateClear();

        QueueEvaluationSafely(TimeSpan.Zero);
        NotifyToolAvailabilityChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        _ = _uiDispatcher.InvokeAsync(
            () =>
            {
                StatusText = GetStateText(State);
                RefreshToolStatusTextForLanguageChange();
                RefreshToolActionTextForLanguageChange();
            },
            CancellationToken.None);
    }

    private void OnSessionStateChanged(
        object? sender,
        SingleViewDeviceSessionStateChangedEventArgs eventArgs)
    {
        if (sender is not ISingleViewDeviceSession session ||
            !ReferenceEquals(session, Volatile.Read(ref _session)) ||
            Volatile.Read(ref _disposed) != 0 ||
            (eventArgs.Previous != SingleViewDeviceSessionState.Running &&
             eventArgs.Current != SingleViewDeviceSessionState.Running))
        {
            return;
        }

        QueueAvailabilityNotification();
    }

    private void QueueAvailabilityNotification()
    {
        try
        {
            if (_uiDispatcher.CheckAccess())
            {
                NotifyAvailabilityPropertiesChanged();
                NotifyCommandStateChanged();
                NotifyToolAvailabilityChanged();
                return;
            }

            _ = NotifyInteractionAvailabilityChangedAsync();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "View Device interaction availability notification failed for {Serial}.",
                Serial);
        }
    }

    private void OnSessionContentSizeChanged(
        object? sender,
        SingleViewDeviceContentSizeChangedEventArgs eventArgs)
    {
        if (sender is not ISingleViewDeviceSession session)
            return;

        ISingleViewDeviceSession? currentSession = Volatile.Read(ref _session);
        if (!ReferenceEquals(session, currentSession) || Volatile.Read(ref _disposed) != 0)
            return;

        _ = SetContentSizeForSessionAsync(
            session,
            eventArgs.Width,
            eventArgs.Height,
            CancellationToken.None);
    }

    private void OnSessionClipboardChanged(
        object? sender,
        ScrcpyClipboardChangedEventArgs eventArgs)
    {
        if (sender is not ISingleViewDeviceSession session)
            return;

        ISingleViewDeviceSession? currentSession = Volatile.Read(ref _session);
        ISingleViewDeviceSession? establishedSession = Volatile.Read(ref _establishedSession);
        if (!ReferenceEquals(session, currentSession) ||
            !ReferenceEquals(session, establishedSession) ||
            Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _ = SynchronizeDeviceClipboardAsync(session, eventArgs.Text);
    }

    private async Task SynchronizeDeviceClipboardAsync(
        ISingleViewDeviceSession session,
        string text)
    {
        try
        {
            await _clipboardSynchronizationGate
                .WaitAsync(_lifetimeCancellation.Token)
                .ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(session, Volatile.Read(ref _session)) ||
                    !ReferenceEquals(session, Volatile.Read(ref _establishedSession)) ||
                    Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                string? currentText = await _clipboardService
                    .GetTextAsync(_lifetimeCancellation.Token)
                    .ConfigureAwait(false);

                // The host read is asynchronous. Serialize the final
                // session validation and clipboard write with lifecycle
                // transitions so a replacement cannot publish stale text.
                await _transitionGate
                    .WaitAsync(_lifetimeCancellation.Token)
                    .ConfigureAwait(false);
                try
                {
                    if (!ReferenceEquals(session, Volatile.Read(ref _session)) ||
                        !ReferenceEquals(session, Volatile.Read(ref _establishedSession)) ||
                        Volatile.Read(ref _disposed) != 0)
                    {
                        return;
                    }

                    if (string.Equals(currentText, text, StringComparison.Ordinal))
                        return;

                    await _clipboardService
                        .SetTextAsync(text, _lifetimeCancellation.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _transitionGate.Release();
                }
            }
            finally
            {
                _clipboardSynchronizationGate.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device clipboard synchronization failed for {Serial}.", Serial);
        }
    }

    private void OnSessionExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not ISingleViewDeviceSession session)
            return;

        ISingleViewDeviceSession? currentSession = Volatile.Read(ref _session);
        if (!ReferenceEquals(session, currentSession) || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _logger.LogInformation("ScrcpyNet session exited for {Serial}; scheduling an isolated restart.", Serial);
        _ = HandleSessionExitedAsync(session);
    }

    private async Task HandleSessionExitedAsync(ISingleViewDeviceSession session)
    {
        bool shouldRestart = false;
        int failureGeneration = -1;
        try
        {
            await _transitionGate
                .WaitAsync(_lifetimeCancellation.Token)
                .ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(session, Volatile.Read(ref _session)) ||
                    !ReferenceEquals(session, Volatile.Read(ref _establishedSession)) ||
                    Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                failureGeneration = Volatile.Read(ref _generation);
                ClearEstablishedSession(session);
                shouldRestart = true;
            }
            finally
            {
                _transitionGate.Release();
            }

            // Deferred UI callbacks must not hold the lifecycle gate. Each
            // callback validates the source session again when it executes.
            try
            {
                await DetachScrcpyClientForSessionAsync(
                        session,
                        session.Client,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "View Device exit unbind failed for {Serial}.",
                    Serial);
            }

            try
            {
                await SetStateForSessionAsync(
                        session,
                        ViewDeviceSessionState.Reconnecting,
                        failureGeneration,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "View Device exit state publication failed for {Serial}.",
                    Serial);
            }

        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device exit handling failed for {Serial}.", Serial);
        }

        // Schedule only after the lifecycle gate is released. Queueing a new
        // evaluation can synchronously re-enter another transition.
        if (shouldRestart)
            ScheduleRestartAfterFailure(failureGeneration, session);
    }

    internal void ScheduleRestartAfterFailure(
        int failureGeneration,
        ISingleViewDeviceSession? sourceSession = null)
    {
        try
        {
            bool exhausted = false;
            TimeSpan delay = default;

            lock (_evaluationSchedulingGate)
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    _lifetimeCancellation.IsCancellationRequested ||
                    failureGeneration != Volatile.Read(ref _generation))
                    return;

                if (sourceSession is not null &&
                    !IsCurrentFailedRecoverySession(sourceSession))
                {
                    return;
                }

                if (_lastRecoveryScheduleGeneration == failureGeneration)
                    return;

                _lastRecoveryScheduleGeneration = failureGeneration;
                int attempt = Interlocked.Increment(ref _restartAttempt) - 1;
                if (attempt >= _restartDelays.Length)
                {
                    exhausted = true;
                }
                else
                {
                    delay = _restartDelays[attempt];
                    // Keep acceptance and generation advancement in one
                    // scheduling critical section. A newer tracker event
                    // cannot slip between validating this failure and
                    // canceling/replacing its pending evaluation.
                    _ = QueueEvaluationAsync(delay);
                }
            }

            if (exhausted)
            {
                _ = TrySetRecoveryFailedStateAsync(failureGeneration, sourceSession);
                return;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device recovery scheduling failed for {Serial}.", Serial);
            _ = TrySetRecoveryFailedStateAsync(failureGeneration, sourceSession);
        }
    }

    private bool IsCurrentFailedRecoverySession(ISingleViewDeviceSession sourceSession)
    {
        return ReferenceEquals(sourceSession, Volatile.Read(ref _session)) &&
               !ReferenceEquals(sourceSession, Volatile.Read(ref _establishedSession)) &&
               sourceSession.State == SingleViewDeviceSessionState.Failed;
    }

    private void QueueEvaluationSafely(
        TimeSpan delay,
        bool resetRestartAttempt = false)
    {
        try
        {
            _ = QueueEvaluationAsync(delay, resetRestartAttempt);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device evaluation queueing failed for {Serial}.", Serial);
            _ = TrySetStateAsync(ViewDeviceSessionState.Failed);
        }
    }

    private static void CancelEvaluation(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task SendBackAsync()
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null)
            return;

        try
        {
            await session.SendBackOrScreenOnAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device Back action failed for {Serial}.", Serial);
        }
    }

    private async Task SendKeyAsync(int keyCode)
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null)
            return;

        try
        {
            await session.SendKeyEventAsync(keyCode, _lifetimeCancellation.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "View Device key action {KeyCode} failed for {Serial}.",
                keyCode,
                Serial);
        }
    }

    private async Task SendPowerAsync()
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null ||
            Interlocked.CompareExchange(ref _powerCooldownActive, 1, 0) != 0)
        {
            return;
        }

        NotifyPowerCommandStateChanged();
        try
        {
            try
            {
                await session
                    .SendKeyEventAsync((int)AndroidKeycode.AKEYCODE_POWER, _lifetimeCancellation.Token)
                    .ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "View Device power action failed for {Serial}.", Serial);
            }

            try
            {
                await _powerCooldownDelay(PowerCooldown, _lifetimeCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
        }
        finally
        {
            Volatile.Write(ref _powerCooldownActive, 0);
            NotifyPowerCommandStateChanged();
        }
    }

    private async Task ScreenOnAsync()
    {
        await SendScreenPowerModeAsync(
            AndroidScreenPowerMode.POWER_MODE_NORMAL,
            "screen on").ConfigureAwait(true);
        await RefreshRuntimeStateAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
    }

    private async Task ScreenOffAsync()
    {
        await SendScreenPowerModeAsync(
            AndroidScreenPowerMode.POWER_MODE_OFF,
            "screen off").ConfigureAwait(true);
        await RefreshRuntimeStateAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
    }

    private async Task SendScreenPowerModeAsync(AndroidScreenPowerMode mode, string actionName)
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null)
            return;

        try
        {
            await session
                .SetScreenPowerModeAsync(mode, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device {ActionName} action failed for {Serial}.", actionName, Serial);
        }
    }

    private async Task PasteHostClipboardAsync()
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null)
            return;

        try
        {
            string? text = await _clipboardService
                .GetTextAsync(_lifetimeCancellation.Token)
                .ConfigureAwait(true);
            if (text is null || !ReferenceEquals(session, GetInteractiveSession()))
                return;

            await session
                .PasteHostClipboardAsync(text, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device clipboard paste failed for {Serial}.", Serial);
        }
    }

    private async Task PasteHostClipboardWithPasteKeyAsync()
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null)
            return;

        try
        {
            string? text = await _clipboardService
                .GetTextAsync(_lifetimeCancellation.Token)
                .ConfigureAwait(true);
            if (text is null || !ReferenceEquals(session, GetInteractiveSession()))
                return;

            await session
                .PasteHostClipboardWithPasteKeyAsync(text, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device native clipboard paste failed for {Serial}.", Serial);
        }
    }

    private async Task InjectHostClipboardAsync()
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null)
            return;

        try
        {
            string? text = await _clipboardService
                .GetTextAsync(_lifetimeCancellation.Token)
                .ConfigureAwait(true);
            if (text is null || !ReferenceEquals(session, GetInteractiveSession()))
                return;

            await session
                .InjectTextAsync(text, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device text injection failed for {Serial}.", Serial);
        }
    }

    private Task RequestClipboardAsync(ScrcpyCopyKey copyKey)
    {
        return SendClipboardRequestAsync(copyKey);
    }

    private async Task SendClipboardRequestAsync(ScrcpyCopyKey copyKey)
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null)
            return;

        try
        {
            await session.RequestClipboardAsync(copyKey, _lifetimeCancellation.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device clipboard request failed for {Serial}.", Serial);
        }
    }

    private Task ExpandNotificationPanelAsync()
    {
        return SendPanelActionAsync(
            session => session.ExpandNotificationPanelAsync(_lifetimeCancellation.Token),
            "notification panel");
    }

    private Task ExpandSettingsPanelAsync()
    {
        return SendPanelActionAsync(
            session => session.ExpandSettingsPanelAsync(_lifetimeCancellation.Token),
            "settings panel");
    }

    private Task CollapsePanelsAsync()
    {
        return SendPanelActionAsync(
            session => session.CollapsePanelsAsync(_lifetimeCancellation.Token),
            "collapse panels");
    }

    private async Task SendPanelActionAsync(
        Func<ISingleViewDeviceSession, Task> action,
        string actionName)
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null)
            return;

        try
        {
            await action(session).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device {ActionName} action failed for {Serial}.", actionName, Serial);
        }
    }

    private async Task SaveScreenshotAsync()
    {
        ISingleViewDeviceSession? session = GetInteractiveSession();
        if (session is null)
            return;

        string defaultName = $"{SanitizeFileName(DeviceName)}-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        string? path = _filePicker.ShowSaveFileDialog(
            _localization.GetString("ViewDevice_ScreenshotFilter"),
            _localization.GetString("ViewDevice_ScreenshotTitle"),
            defaultName);
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!ReferenceEquals(session, GetInteractiveSession()))
            return;

        try
        {
            await _screenshotService
                .CapturePngAsync(Serial, path, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device screenshot failed for {Serial}.", Serial);
        }
    }

    private void NotifyPowerCommandStateChanged()
    {
        try
        {
            _ = _uiDispatcher.InvokeAsync(PowerCommand.NotifyCanExecuteChanged, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "View Device power command state notification failed for {Serial}.", Serial);
        }
    }

    private async Task NotifyInteractionAvailabilityChangedAsync()
    {
        try
        {
            await _uiDispatcher
                .InvokeAsync(
                    () =>
                    {
                        NotifyAvailabilityPropertiesChanged();
                        NotifyCommandStateChanged();
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "View Device interaction command state notification failed for {Serial}.",
                Serial);
        }
    }

    private async Task TrySetStateAsync(ViewDeviceSessionState state)
    {
        try
        {
            await SetStateAsync(state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device state publication failed for {Serial}.", Serial);
        }
    }

    private async Task TrySetRecoveryFailedStateAsync(
        int expectedGeneration,
        ISingleViewDeviceSession? expectedSession = null)
    {
        try
        {
            await SetRecoveryFailedStateAsync(expectedGeneration, expectedSession)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "View Device recovery failure state publication failed for {Serial}.",
                Serial);
        }
    }

    private async Task TrySetBusyStateAsync(int expectedGeneration)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(
                    () =>
                    {
                        if (Volatile.Read(ref _disposed) != 0 ||
                            _lifetimeCancellation.IsCancellationRequested ||
                            expectedGeneration != Volatile.Read(ref _generation))
                        {
                            return;
                        }

                        State = ViewDeviceSessionState.Busy;
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device Busy state publication failed for {Serial}.", Serial);
        }
    }

    private async Task TryEmergencyDetachScrcpyClientAsync()
    {
        try
        {
            await _uiDispatcher.InvokeAsync(
                    () =>
                    {
                        // A current session owns its published client. This
                        // fallback is used only after owned cleanup has failed
                        // or disposal has started, so it cannot clear a live
                        // replacement session.
                        if (Volatile.Read(ref _disposed) != 0 ||
                            Volatile.Read(ref _session) is null)
                        {
                            ScrcpyClient = null;
                        }
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device client cleanup publication failed for {Serial}.", Serial);
        }
    }

    private Task SetStateAsync(ViewDeviceSessionState state, CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() => State = state, cancellationToken);
    }

    private Task SetRecoveryFailedStateAsync(
        int expectedGeneration,
        ISingleViewDeviceSession? expectedSession = null)
    {
        return _uiDispatcher.InvokeAsync(
            () =>
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    _lifetimeCancellation.IsCancellationRequested ||
                    expectedGeneration != Volatile.Read(ref _generation))
                {
                    return;
                }

                if (expectedSession is not null &&
                    !IsCurrentFailedRecoverySession(expectedSession))
                {
                    return;
                }

                State = ViewDeviceSessionState.Failed;
            },
            CancellationToken.None);
    }

    private Task DetachScrcpyClientForSessionAsync(
        ISingleViewDeviceSession sourceSession,
        ScrcpyNet.Scrcpy? expectedClient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        return _uiDispatcher.InvokeAsync(
            () =>
            {
                if (!ReferenceEquals(sourceSession, Volatile.Read(ref _session)) ||
                    !ReferenceEquals(expectedClient, sourceSession.Client) ||
                    !ReferenceEquals(expectedClient, ScrcpyClient))
                {
                    return;
                }

                if (expectedClient is not null)
                    ScrcpyClient = null;
            },
            cancellationToken);
    }

    private Task SetStateForSessionAsync(
        ISingleViewDeviceSession sourceSession,
        ViewDeviceSessionState state,
        int expectedGeneration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        return _uiDispatcher.InvokeAsync(
            () =>
            {
                if (!ReferenceEquals(sourceSession, Volatile.Read(ref _session)) ||
                    expectedGeneration != Volatile.Read(ref _generation) ||
                    Volatile.Read(ref _disposed) != 0)
                    return;

                State = state;
            },
            cancellationToken);
    }

    private Task SetManualReconnectStateForSessionAsync(
        ISingleViewDeviceSession sourceSession,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        return _uiDispatcher.InvokeAsync(
            () =>
            {
                // Manual reconnect owns the transition gate. A tracker event may
                // advance the automatic-evaluation generation while this UI
                // callback is queued, but it must not leave the accepted source
                // session displayed as Running while teardown has begun.
                if (!ReferenceEquals(sourceSession, Volatile.Read(ref _session)) ||
                    !ReferenceEquals(sourceSession, Volatile.Read(ref _establishedSession)) ||
                    Volatile.Read(ref _manualReconnectActive) == 0 ||
                    Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                State = ViewDeviceSessionState.Reconnecting;
            },
            cancellationToken);
    }

    private Task SetContentSizeForSessionAsync(
        ISingleViewDeviceSession sourceSession,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        if (width <= 0 || height <= 0)
            return Task.CompletedTask;

        return _uiDispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(sourceSession, Volatile.Read(ref _session)) ||
                !ReferenceEquals(sourceSession, Volatile.Read(ref _establishedSession)) ||
                sourceSession.State != SingleViewDeviceSessionState.Running ||
                Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (ContentWidth == width && ContentHeight == height)
                return;

            ContentWidth = width;
            ContentHeight = height;
            OnPropertyChanged(nameof(DeviceAspectRatio));
            OnPropertyChanged(nameof(ViewAspectRatio));
            NotifyToolCommandStateChanged();
        }, cancellationToken);
    }

    private bool ClearEstablishedSession(ISingleViewDeviceSession expectedSession)
    {
        ArgumentNullException.ThrowIfNull(expectedSession);
        ISingleViewDeviceSession? previous = Interlocked.CompareExchange(
            ref _establishedSession,
            null,
            expectedSession);
        if (!ReferenceEquals(previous, expectedSession))
            return false;

        QueueAvailabilityNotification();
        return true;
    }

    private ISingleViewDeviceSession? GetInteractiveSession()
    {
        ISingleViewDeviceSession? currentSession = Volatile.Read(ref _session);
        ISingleViewDeviceSession? establishedSession = Volatile.Read(ref _establishedSession);
        if (!IsInteractiveSessionSnapshot(
                State,
                currentSession,
                establishedSession,
                Volatile.Read(ref _disposed) != 0))
            return null;

        return currentSession;
    }

    internal static bool IsInteractiveSessionSnapshot(
        ViewDeviceSessionState viewState,
        ISingleViewDeviceSession? currentSession,
        ISingleViewDeviceSession? establishedSession,
        bool disposed)
    {
        return !disposed &&
               viewState == ViewDeviceSessionState.Running &&
               currentSession is not null &&
               currentSession.State == SingleViewDeviceSessionState.Running &&
               ReferenceEquals(currentSession, establishedSession);
    }

    private bool CanInteract()
    {
        return GetInteractiveSession() is not null;
    }

    private bool CanSendPower()
    {
        return CanInteract() && Volatile.Read(ref _powerCooldownActive) == 0;
    }

    private void NotifyAvailabilityPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsUnavailable));
    }


    private bool CanRetry()
    {
        return State is ViewDeviceSessionState.WaitingForDevice or
            ViewDeviceSessionState.Unauthorized or
            ViewDeviceSessionState.AdbUnavailable or
            ViewDeviceSessionState.Busy or
            ViewDeviceSessionState.Reconnecting or
            ViewDeviceSessionState.Failed;
    }

    private bool CanReconnect()
    {
        return GetInteractiveSession() is not null &&
               Volatile.Read(ref _manualReconnectActive) == 0 &&
               Volatile.Read(ref _disposed) == 0;
    }

    private void NotifyCommandStateChanged()
    {
        RetryCommand.NotifyCanExecuteChanged();
        ReconnectCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        HomeCommand.NotifyCanExecuteChanged();
        RecentCommand.NotifyCanExecuteChanged();
        PowerCommand.NotifyCanExecuteChanged();
        VolumeUpCommand.NotifyCanExecuteChanged();
        VolumeDownCommand.NotifyCanExecuteChanged();
        ScreenOnCommand.NotifyCanExecuteChanged();
        ScreenOffCommand.NotifyCanExecuteChanged();
        ScreenshotCommand.NotifyCanExecuteChanged();
        PasteHostClipboardCommand.NotifyCanExecuteChanged();
        PasteHostClipboardWithPasteKeyCommand.NotifyCanExecuteChanged();
        NotifyToolCommandStateChanged();
        InjectHostClipboardCommand.NotifyCanExecuteChanged();
        CopyClipboardCommand.NotifyCanExecuteChanged();
        CutClipboardCommand.NotifyCanExecuteChanged();
        ExpandNotificationPanelCommand.NotifyCanExecuteChanged();
        ExpandSettingsPanelCommand.NotifyCanExecuteChanged();
        CollapsePanelsCommand.NotifyCanExecuteChanged();
        MenuCommand.NotifyCanExecuteChanged();
    }

    private string GetStateText(ViewDeviceSessionState state)
    {
        string key = state switch
        {
            ViewDeviceSessionState.Created => "ViewDevice_StatusChecking",
            ViewDeviceSessionState.CheckingDevice => "ViewDevice_StatusChecking",
            ViewDeviceSessionState.Starting => "ViewDevice_StatusStarting",
            ViewDeviceSessionState.Running => "ViewDevice_StatusRunning",
            ViewDeviceSessionState.WaitingForDevice => "ViewDevice_StatusWaitingForDevice",
            ViewDeviceSessionState.Unauthorized => "ViewDevice_StatusUnauthorized",
            ViewDeviceSessionState.AdbUnavailable => "ViewDevice_StatusAdbUnavailable",
            ViewDeviceSessionState.Busy => "ViewDevice_StatusBusy",
            ViewDeviceSessionState.Reconnecting => "ViewDevice_StatusReconnecting",
            ViewDeviceSessionState.Failed => "ViewDevice_StatusFailed",
            ViewDeviceSessionState.Closing => "ViewDevice_StatusClosing",
            ViewDeviceSessionState.Closed => "ViewDevice_StatusClosed",
            _ => "ViewDevice_StatusFailed"
        };
        return _localization.GetString(key);
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }
}
