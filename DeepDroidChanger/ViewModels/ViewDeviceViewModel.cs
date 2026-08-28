using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels;

public sealed class ViewDeviceViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan DeviceOnlineDebounce = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan[] RestartDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    private readonly IViewDeviceSessionFactory _sessionFactory;
    private readonly IAdbDeviceTrackerService _deviceTracker;
    private readonly IAdbCommandService _adbCommandService;
    private readonly IFilePickerDialogService _filePicker;
    private readonly IViewDeviceScreenshotService _screenshotService;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcherService _uiDispatcher;
    private readonly ILogger<ViewDeviceViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _evaluationSchedulingGate = new();
    private CancellationTokenSource? _pendingEvaluationCancellation;
    private Task _pendingEvaluationTask = Task.CompletedTask;
    private IViewDeviceSession? _session;
    private string _serial = string.Empty;
    private string _deviceName = string.Empty;
    private ViewDeviceSessionState _state = ViewDeviceSessionState.Created;
    private string _statusText = string.Empty;
    private IntPtr _nativeWindowHandle;
    private int _contentWidth;
    private int _contentHeight;
    private bool _isActionsPanelExpanded;
    private bool _isFullscreen;
    private bool _hasRun;
    private int _restartAttempt;
    private int _generation;
    private int _manualReconnectActive;
    private int _disposed;

    public ViewDeviceViewModel(
        IViewDeviceSessionFactory sessionFactory,
        IAdbDeviceTrackerService deviceTracker,
        IAdbCommandService adbCommandService,
        IFilePickerDialogService filePicker,
        IViewDeviceScreenshotService screenshotService,
        ILocalizationService localization,
        IUiDispatcherService uiDispatcher,
        ILogger<ViewDeviceViewModel> logger)
    {
        _sessionFactory = sessionFactory;
        _deviceTracker = deviceTracker;
        _adbCommandService = adbCommandService;
        _filePicker = filePicker;
        _screenshotService = screenshotService;
        _localization = localization;
        _uiDispatcher = uiDispatcher;
        _logger = logger;

        RetryCommand = new AsyncRelayCommand(RetryAsync, CanRetry);
        ReconnectCommand = new AsyncRelayCommand(ReconnectAsync, CanReconnect);
        BackCommand = new AsyncRelayCommand(() => SendKeyAsync(4), CanInteract);
        HomeCommand = new AsyncRelayCommand(() => SendKeyAsync(3), CanInteract);
        RecentCommand = new AsyncRelayCommand(() => SendKeyAsync(187), CanInteract);
        PowerCommand = new AsyncRelayCommand(() => SendKeyAsync(26), CanInteract);
        VolumeUpCommand = new AsyncRelayCommand(() => SendKeyAsync(24), CanInteract);
        VolumeDownCommand = new AsyncRelayCommand(() => SendKeyAsync(25), CanInteract);
        SendTextCommand = new AsyncRelayCommand<string?>(SendTextAsync, _ => CanInteract());
        SendEnterCommand = new AsyncRelayCommand(() => SendKeyAsync(66), CanInteract);
        RunAdbShellCommand = new AsyncRelayCommand<string?>(RunAdbShellAsync, _ => CanInteract());
        ScreenshotCommand = new AsyncRelayCommand(SaveScreenshotAsync, CanInteract);
        ToggleActionsPanelCommand = new RelayCommand(() => IsActionsPanelExpanded = !IsActionsPanelExpanded);
        ToggleFullscreenCommand = new RelayCommand(() => IsFullscreen = !IsFullscreen);
        StatusText = GetStateText(State);
    }

    public event EventHandler? NativeWindowHandleChanged;
    public event EventHandler? NativeFocusRequested;

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
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsUnavailable));
            NotifyCommandStateChanged();
        }
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

    public bool IsRunning => State == ViewDeviceSessionState.Running;
    public bool IsUnavailable => !IsRunning;

    public bool IsActionsPanelExpanded
    {
        get => _isActionsPanelExpanded;
        set => SetProperty(ref _isActionsPanelExpanded, value);
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => SetProperty(ref _isFullscreen, value);
    }

    public IAsyncRelayCommand RetryCommand { get; }
    public IAsyncRelayCommand ReconnectCommand { get; }
    public IAsyncRelayCommand BackCommand { get; }
    public IAsyncRelayCommand HomeCommand { get; }
    public IAsyncRelayCommand RecentCommand { get; }
    public IAsyncRelayCommand PowerCommand { get; }
    public IAsyncRelayCommand VolumeUpCommand { get; }
    public IAsyncRelayCommand VolumeDownCommand { get; }
    public IAsyncRelayCommand<string?> SendTextCommand { get; }
    public IAsyncRelayCommand SendEnterCommand { get; }
    public IAsyncRelayCommand<string?> RunAdbShellCommand { get; }
    public IAsyncRelayCommand ScreenshotCommand { get; }
    public IRelayCommand ToggleActionsPanelCommand { get; }
    public IRelayCommand ToggleFullscreenCommand { get; }

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

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        try
        {
            await _deviceTracker.StartAsync(linked.Token).ConfigureAwait(true);
            await QueueEvaluationAsync(TimeSpan.Zero, resetRestartAttempt: true).ConfigureAwait(true);
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

        await _transitionGate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            State = ViewDeviceSessionState.Closing;
            await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(true);
            State = ViewDeviceSessionState.Closed;
        }
        finally
        {
            _transitionGate.Release();
            _transitionGate.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }

    internal async Task HandleNativeHostFailureAsync(Exception exception)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _logger.LogWarning(exception, "Failed to embed the scrcpy window for {Serial}.", Serial);
        Interlocked.Increment(ref _generation);

        await _transitionGate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(true);
            await SetStateAsync(ViewDeviceSessionState.Failed, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            _transitionGate.Release();
        }

        ScheduleRestartAfterFailure();
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
        try
        {
            int generation = CancelPendingEvaluation(resetRestartAttempt: true);
            await _transitionGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    _session?.State != ViewDeviceSessionState.Running)
                {
                    return;
                }

                await StopCurrentSessionAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
                if (Volatile.Read(ref _disposed) != 0)
                    return;

                await StartSessionAsync(
                        generation,
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
            await SetStateAsync(ViewDeviceSessionState.Failed, CancellationToken.None).ConfigureAwait(false);
            ScheduleRestartAfterFailure();
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
        finally
        {
            if (ReferenceEquals(Volatile.Read(ref _pendingEvaluationCancellation), cancellation))
                Interlocked.CompareExchange(ref _pendingEvaluationCancellation, null, cancellation);
            cancellation.Dispose();
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
                if (_session?.State == ViewDeviceSessionState.Running)
                    return;

                await SetStateAsync(ViewDeviceSessionState.AdbUnavailable, cancellationToken).ConfigureAwait(false);
                return;
            }

            AdbDevice? device = _deviceTracker.GetDevice(Serial);
            if (device?.Status == AdbDeviceStatus.Unauthorized)
            {
                await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                await SetStateAsync(ViewDeviceSessionState.Unauthorized, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (device?.Status != AdbDeviceStatus.Online)
            {
                await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                await SetStateAsync(ViewDeviceSessionState.WaitingForDevice, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_session?.State == ViewDeviceSessionState.Running)
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
                ScheduleRestartAfterFailure();
            return;
        }

        IViewDeviceSession session = _sessionFactory.Create(new ViewDeviceLaunchOptions(Serial));
        _session = session;
        session.NativeWindowReady += OnSessionNativeWindowReady;
        session.ContentSizeChanged += OnSessionContentSizeChanged;
        session.Exited += OnSessionExited;

        try
        {
            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            if (requireCurrentGeneration && generation != Volatile.Read(ref _generation))
            {
                await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }

            _hasRun = true;
            Interlocked.Exchange(ref _restartAttempt, 0);
            await SetNativeWindowAsync(session.NativeWindowHandle, cancellationToken).ConfigureAwait(false);
            await SetContentSizeAsync(session.ContentWidth, session.ContentHeight, cancellationToken).ConfigureAwait(false);
            await SetStateAsync(ViewDeviceSessionState.Running, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Official scrcpy startup failed for {Serial}. Diagnostics: {Diagnostics}",
                Serial,
                string.Join(" | ", session.RecentDiagnostics));
            await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
            await SetStateAsync(ViewDeviceSessionState.Failed, CancellationToken.None).ConfigureAwait(false);
            ScheduleRestartAfterFailure();
        }
    }

    private async Task StopCurrentSessionAsync(CancellationToken cancellationToken)
    {
        IViewDeviceSession? session = _session;
        if (session is null)
        {
            await SetNativeWindowAsync(IntPtr.Zero, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        _session = null;
        session.NativeWindowReady -= OnSessionNativeWindowReady;
        session.ContentSizeChanged -= OnSessionContentSizeChanged;
        session.Exited -= OnSessionExited;
        try
        {
            await session.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await SetNativeWindowAsync(IntPtr.Zero, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private void OnDeviceStateChanged(object? sender, AdbDeviceStateChangedEventArgs eventArgs)
    {
        if (!string.Equals(eventArgs.Serial, Serial, StringComparison.OrdinalIgnoreCase))
            return;

        TimeSpan delay = eventArgs.Current?.Status == AdbDeviceStatus.Online
            ? DeviceOnlineDebounce
            : TimeSpan.Zero;
        _ = QueueEvaluationAsync(delay, resetRestartAttempt: eventArgs.Current?.Status == AdbDeviceStatus.Online);
    }

    private void OnTrackerHealthChanged(object? sender, AdbDeviceTrackerHealthChangedEventArgs eventArgs)
    {
        _ = QueueEvaluationAsync(TimeSpan.Zero);
    }

    private void OnSessionNativeWindowReady(object? sender, EventArgs eventArgs)
    {
        if (sender is IViewDeviceSession session && ReferenceEquals(session, _session))
            _ = SetNativeWindowAsync(session.NativeWindowHandle, CancellationToken.None);
    }

    private void OnSessionContentSizeChanged(object? sender, ViewDeviceContentSizeChangedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, _session))
            _ = SetContentSizeAsync(eventArgs.Width, eventArgs.Height, CancellationToken.None);
    }

    private void OnSessionExited(object? sender, EventArgs eventArgs)
    {
        if (!ReferenceEquals(sender, _session) || Volatile.Read(ref _disposed) != 0)
            return;

        _logger.LogInformation("Official scrcpy process exited for {Serial}; scheduling an isolated restart.", Serial);
        _ = SetNativeWindowAsync(IntPtr.Zero, CancellationToken.None);
        _ = SetStateAsync(ViewDeviceSessionState.Reconnecting, CancellationToken.None);
        ScheduleRestartAfterFailure();
    }

    private void ScheduleRestartAfterFailure()
    {
        int attempt = Math.Min(Interlocked.Increment(ref _restartAttempt) - 1, RestartDelays.Length - 1);
        _ = QueueEvaluationAsync(RestartDelays[attempt]);
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

    private async Task SendKeyAsync(int keyCode)
    {
        if (!CanInteract())
            return;

        try
        {
            await _adbCommandService
                .SendKeyEventAsync(Serial, keyCode, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            NativeFocusRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device key action failed for {Serial}.", Serial);
        }
    }

    private async Task SendTextAsync(string? text)
    {
        if (!CanInteract() || string.IsNullOrEmpty(text))
            return;

        try
        {
            await _adbCommandService
                .SendTextAsync(Serial, text, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            NativeFocusRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device text input failed for {Serial}.", Serial);
        }
    }

    private async Task RunAdbShellAsync(string? command)
    {
        if (!CanInteract() || string.IsNullOrWhiteSpace(command))
            return;

        try
        {
            CommandResult result = await _adbCommandService
                .RunAdbShellAsync(Serial, command.Trim(), _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            if (result.ExitCode != 0)
            {
                _logger.LogWarning(
                    "View Device ADB shell command exited with code {ExitCode} for {Serial}.",
                    result.ExitCode,
                    Serial);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Device ADB shell command failed for {Serial}.", Serial);
        }
    }

    private async Task SaveScreenshotAsync()
    {
        if (!CanInteract())
            return;

        string defaultName = $"{SanitizeFileName(DeviceName)}-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        string? path = _filePicker.ShowSaveFileDialog(
            _localization.GetString("ViewDevice_ScreenshotFilter"),
            _localization.GetString("ViewDevice_ScreenshotTitle"),
            defaultName);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            await _screenshotService
                .CapturePngAsync(Serial, path, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            NativeFocusRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "View Device screenshot failed for {Serial}.", Serial);
        }
    }

    private Task SetStateAsync(ViewDeviceSessionState state, CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() => State = state, cancellationToken);
    }

    private Task SetNativeWindowAsync(IntPtr handle, CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() => NativeWindowHandle = handle, cancellationToken);
    }

    private Task SetContentSizeAsync(int width, int height, CancellationToken cancellationToken)
    {
        if (width <= 0 || height <= 0)
            return Task.CompletedTask;

        return _uiDispatcher.InvokeAsync(() =>
        {
            ContentWidth = width;
            ContentHeight = height;
            OnPropertyChanged(nameof(DeviceAspectRatio));
        }, cancellationToken);
    }

    private bool CanInteract()
    {
        return State == ViewDeviceSessionState.Running && Volatile.Read(ref _disposed) == 0;
    }

    private bool CanRetry()
    {
        return State is ViewDeviceSessionState.WaitingForDevice or
            ViewDeviceSessionState.Unauthorized or
            ViewDeviceSessionState.AdbUnavailable or
            ViewDeviceSessionState.Reconnecting or
            ViewDeviceSessionState.Failed;
    }

    private bool CanReconnect()
    {
        return State == ViewDeviceSessionState.Running &&
               _session?.State == ViewDeviceSessionState.Running &&
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
        SendTextCommand.NotifyCanExecuteChanged();
        SendEnterCommand.NotifyCanExecuteChanged();
        RunAdbShellCommand.NotifyCanExecuteChanged();
        ScreenshotCommand.NotifyCanExecuteChanged();
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
