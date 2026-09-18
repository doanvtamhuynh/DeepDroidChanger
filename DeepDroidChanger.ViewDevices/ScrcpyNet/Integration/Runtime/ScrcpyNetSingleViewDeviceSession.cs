using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrcpyNet;
using SharpAdbClient;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetSingleViewDeviceSession : ISingleViewDeviceSession
{
    private const int DiagnosticCapacity = 64;
    private static readonly TimeSpan DefaultFirstFrameTimeout = TimeSpan.FromSeconds(5);

    private readonly ViewDeviceLaunchOptions _options;
    private readonly ISharpAdbDeviceResolver _deviceResolver;
    private readonly IScrcpyNetClientFactory _clientFactory;
    private readonly ILogger<ScrcpyNetSingleViewDeviceSession> _logger;
    private readonly object _gate = new();
    private readonly Queue<string> _diagnostics = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly TimeSpan _firstFrameTimeout;

    private IScrcpyNetClient? _client;
    private TaskCompletionSource? _firstFrameReceived;
    private SingleViewDeviceSessionState _state = SingleViewDeviceSessionState.Created;
    private int _contentWidth;
    private int _contentHeight;
    private int _intentionalStop = 1;
    // Owned by _gate so stale callbacks cannot consume a new lifecycle marker.
    private int _unexpectedExitReported;
    private int _disposed;

    public ScrcpyNetSingleViewDeviceSession(
        ViewDeviceLaunchOptions options,
        ISharpAdbDeviceResolver deviceResolver,
        IScrcpyNetClientFactory clientFactory,
        ILogger<ScrcpyNetSingleViewDeviceSession> logger,
        TimeSpan? firstFrameTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(deviceResolver);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _deviceResolver = deviceResolver;
        _clientFactory = clientFactory;
        _logger = logger;
        _firstFrameTimeout = firstFrameTimeout ?? DefaultFirstFrameTimeout;
        if (_firstFrameTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(firstFrameTimeout));
        if (string.IsNullOrWhiteSpace(options.Serial))
            throw new ArgumentException("A serial is required for a Single View session.", nameof(options));
    }

    public string Serial => _options.Serial;

    public SingleViewDeviceSessionState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public Scrcpy? Client
    {
        get
        {
            lock (_gate)
                return _client?.Client;
        }
    }

    public int ContentWidth
    {
        get
        {
            lock (_gate)
                return _contentWidth;
        }
    }

    public int ContentHeight
    {
        get
        {
            lock (_gate)
                return _contentHeight;
        }
    }

    public IReadOnlyList<string> RecentDiagnostics
    {
        get
        {
            lock (_gate)
                return _diagnostics.ToArray();
        }
    }

    public event EventHandler<SingleViewDeviceSessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<SingleViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
    public event EventHandler<ScrcpyClipboardChangedEventArgs>? ClipboardChanged;
    public event EventHandler? Exited;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State == SingleViewDeviceSessionState.Running)
                return;
            if (GetCurrentClient() is not null)
                throw new InvalidOperationException("The Single View session has already been started.");

            SetState(SingleViewDeviceSessionState.Starting);
            try
            {
                // Resolve performs synchronous SharpAdb status/start/device-list
                // calls. This token can prevent scheduling, but cannot interrupt
                // those calls once started, so always await the worker to its
                // terminal state instead of abandoning it on a timeout race.
                DeviceData device = await Task.Run(
                        () => _deviceResolver.Resolve(Serial),
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                IScrcpyNetClient client = _clientFactory.Create(device, _options);
                TaskCompletionSource firstFrameReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_gate)
                {
                    _client = client;
                    _firstFrameReceived = firstFrameReceived;
                    _contentWidth = 0;
                    _contentHeight = 0;
                    Volatile.Write(ref _intentionalStop, 0);
                    _unexpectedExitReported = 0;
                }
                SubscribeToClient(client);

                try
                {
                    await client.StartAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!client.Connected)
                        throw new InvalidOperationException("ScrcpyNet did not report a connected session after start.");
                    try
                    {
                        await firstFrameReceived.Task
                            .WaitAsync(_firstFrameTimeout, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException exception)
                    {
                        throw new TimeoutException(
                            "Timed out waiting for the first decoded frame from ScrcpyNet.",
                            exception);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch
                {
                    await CleanupClientAsync().ConfigureAwait(false);
                    throw;
                }

                if (!HasValidContentSize())
                {
                    throw new InvalidOperationException(
                        "The first decoded frame did not provide valid content dimensions.");
                }

                if (!TryTransitionToRunning(
                        client,
                        out SingleViewDeviceSessionState observedState))
                {
                    if (observedState == SingleViewDeviceSessionState.Failed)
                    {
                        throw new InvalidOperationException(
                            "The ScrcpyNet client exited while the session was starting.");
                    }

                    throw new InvalidOperationException(
                        $"The session entered an unexpected state ({observedState}) while starting.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CleanupClientAsync().ConfigureAwait(false);
                SetState(SingleViewDeviceSessionState.Closed);
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception exception)
            {
                AddDiagnostic(exception.ToString());
                await CleanupClientAsync().ConfigureAwait(false);
                SetState(SingleViewDeviceSessionState.Failed);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task FlushControlAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? unavailable = RequireInteractiveClient("control flush", out IScrcpyNetClient? client);
        if (unavailable is not null)
            return unavailable;

        return client!.FlushControlAsync(cancellationToken);
    }

    public Task SendKeyEventAsync(int keyCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? unavailable = RequireInteractiveClient("key press", out IScrcpyNetClient? client);
        if (unavailable is not null)
            return unavailable;

        return SendCriticalBatch(
            client!,
            [
                new KeycodeControlMessage
                {
                    Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN,
                    KeyCode = (AndroidKeycode)keyCode
                },
                new KeycodeControlMessage
                {
                    Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
                    KeyCode = (AndroidKeycode)keyCode
                }
            ],
            "key press");
    }

    public Task SendBackOrScreenOnAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? unavailable = RequireInteractiveClient(
            "Back or screen-on",
            out IScrcpyNetClient? client);
        if (unavailable is not null)
            return unavailable;

        return SendCriticalBatch(
            client!,
            [
                new BackOrScreenOnControlMessage
                {
                    Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN
                },
                new BackOrScreenOnControlMessage
                {
                    Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP
                }
            ],
            "Back or screen-on");
    }

    public Task SetScreenPowerModeAsync(
        AndroidScreenPowerMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? unavailable = RequireInteractiveClient(
            "screen power mode",
            out IScrcpyNetClient? client);
        if (unavailable is not null)
            return unavailable;

        return SendCriticalBatch(
            client!,
            [new SetScreenPowerModeControlMessage { Mode = mode }],
            "screen power mode");
    }

    public Task RotateDeviceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? unavailable = RequireInteractiveClient(
            "device rotation",
            out IScrcpyNetClient? client);
        if (unavailable is not null)
            return unavailable;

        return SendCriticalBatch(
            client!,
            [new RotateDeviceControlMessage()],
            "device rotation");
    }

    public Task PasteHostClipboardAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        return PasteClipboardNativeAsync(text, cancellationToken, "Ctrl+V clipboard paste");
    }

    public Task PasteHostClipboardWithPasteKeyAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        return PasteClipboardNativeAsync(text, cancellationToken, "native clipboard paste");
    }

    private Task PasteClipboardNativeAsync(
        string text,
        CancellationToken cancellationToken,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length == 0)
            return Task.CompletedTask;

        Task? unavailable = RequireInteractiveClient(operation, out IScrcpyNetClient? client);
        if (unavailable is not null)
            return unavailable;

        return SendCriticalBatch(
            client!,
            [
                new SetClipboardControlMessage
                {
                    Sequence = 0,
                    Paste = true,
                    Text = text
                }
            ],
            operation);
    }

    public Task InjectTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        Task? unavailable = RequireInteractiveClient(
            "clipboard text injection",
            out IScrcpyNetClient? client);
        if (unavailable is not null)
            return unavailable;

        bool accepted = client!.TrySendControlCommands(
        [
            new InjectTextControlMessage { Text = text }
        ]);
        _logger.LogDebug(
            "Single View clipboard text injection requested for {Serial}; chars={CharacterCount}, utf8Bytes={Utf8ByteCount}, sessionRunning={SessionRunning}, controlBatchAccepted={ControlBatchAccepted}.",
            Serial,
            text.Length,
            Encoding.UTF8.GetByteCount(text),
            true,
            accepted);
        return accepted
            ? Task.CompletedTask
            : RejectedControlBatch("clipboard text injection");
    }

    public Task RequestClipboardAsync(
        ScrcpyCopyKey copyKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? unavailable = RequireInteractiveClient(
            "clipboard request",
            out IScrcpyNetClient? client);
        if (unavailable is not null)
            return unavailable;

        return SendCriticalBatch(
            client!,
            [new GetClipboardControlMessage { CopyKey = copyKey }],
            "clipboard request");
    }

    public Task ExpandNotificationPanelAsync(CancellationToken cancellationToken = default)
    {
        return SendPanelCommandAsync(new ExpandNotificationPanelControlMessage(), cancellationToken);
    }

    public Task ExpandSettingsPanelAsync(CancellationToken cancellationToken = default)
    {
        return SendPanelCommandAsync(new ExpandSettingsPanelControlMessage(), cancellationToken);
    }

    public Task CollapsePanelsAsync(CancellationToken cancellationToken = default)
    {
        return SendPanelCommandAsync(new CollapsePanelsControlMessage(), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Dispose();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        IScrcpyNetClient? client;
        lock (_gate)
        {
            client = _client;
            Volatile.Write(ref _intentionalStop, 1);
        }

        if (client is null)
        {
            if (State != SingleViewDeviceSessionState.Closed)
                SetState(SingleViewDeviceSessionState.Closed);
            return;
        }

        SetState(SingleViewDeviceSessionState.Closing);
        UnsubscribeFromClient(client);
        Exception? failure = null;
        try
        {
            await client.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await client.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    failure = null;
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "ScrcpyNet cleanup after canceled stop failed for {Serial}.",
                        Serial);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_client, client))
                    _client = null;
            }

            try
            {
                client.Dispose();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "ScrcpyNet client disposal failed for {Serial}.", Serial);
                failure ??= exception;
            }

            SetState(SingleViewDeviceSessionState.Closed);
        }

        if (failure is not null)
            throw failure;
    }

    private async Task CleanupClientAsync()
    {
        IScrcpyNetClient? client;
        lock (_gate)
        {
            client = _client;
            _client = null;
            _firstFrameReceived?.TrySetCanceled();
            _firstFrameReceived = null;
            Volatile.Write(ref _intentionalStop, 1);
        }

        if (client is null)
            return;

        UnsubscribeFromClient(client);
        try
        {
            await client.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "ScrcpyNet failed while cleaning up {Serial}.", Serial);
        }
        finally
        {
            try
            {
                client.Dispose();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "ScrcpyNet disposal failed while cleaning up {Serial}.", Serial);
            }
        }
    }

    private void SubscribeToClient(IScrcpyNetClient client)
    {
        client.FrameReceived += OnClientFrameReceived;
        client.Failed += OnClientFailed;
        client.Exited += OnClientExited;
        client.ClipboardChanged += OnClientClipboardChanged;
    }

    private void UnsubscribeFromClient(IScrcpyNetClient client)
    {
        client.FrameReceived -= OnClientFrameReceived;
        client.Failed -= OnClientFailed;
        client.Exited -= OnClientExited;
        client.ClipboardChanged -= OnClientClipboardChanged;
    }

    private void OnClientFrameReceived(object? sender, ScrcpyNetFrameEventArgs eventArgs)
    {
        if (sender is not IScrcpyNetClient client)
            return;

        SingleViewDeviceContentSizeChangedEventArgs? sizeChanged = null;
        lock (_gate)
        {
            if (!ReferenceEquals(_client, client) ||
                Volatile.Read(ref _intentionalStop) != 0 ||
                Volatile.Read(ref _disposed) != 0 ||
                (_state != SingleViewDeviceSessionState.Starting &&
                 _state != SingleViewDeviceSessionState.Running) ||
                eventArgs.Width <= 0 ||
                eventArgs.Height <= 0)
                return;

            if (_contentWidth != eventArgs.Width || _contentHeight != eventArgs.Height)
            {
                _contentWidth = eventArgs.Width;
                _contentHeight = eventArgs.Height;
                sizeChanged = new SingleViewDeviceContentSizeChangedEventArgs(
                    eventArgs.Width,
                    eventArgs.Height);
            }

            _firstFrameReceived?.TrySetResult();
        }

        if (sizeChanged is not null)
            InvokeSafely(ContentSizeChanged, sizeChanged);
    }

    private void OnClientClipboardChanged(
        object? sender,
        ScrcpyClipboardChangedEventArgs eventArgs)
    {
        if (sender is not IScrcpyNetClient client)
            return;

        lock (_gate)
        {
            if (!ReferenceEquals(_client, client) ||
                Volatile.Read(ref _intentionalStop) != 0 ||
                Volatile.Read(ref _disposed) != 0 ||
                (_state != SingleViewDeviceSessionState.Starting &&
                 _state != SingleViewDeviceSessionState.Running))
                return;
        }

        InvokeSafely(ClipboardChanged, eventArgs);
    }

    private void OnClientFailed(object? sender, ScrcpyNetErrorEventArgs eventArgs)
    {
        if (sender is not IScrcpyNetClient client)
            return;

        lock (_gate)
        {
            if (!ReferenceEquals(_client, client) ||
                Volatile.Read(ref _intentionalStop) != 0 ||
                Volatile.Read(ref _disposed) != 0 ||
                (_state != SingleViewDeviceSessionState.Starting &&
                 _state != SingleViewDeviceSessionState.Running))
                return;

            AddDiagnosticLocked(eventArgs.Exception.ToString());
        }
    }

    private void OnClientExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not IScrcpyNetClient client)
        {
            return;
        }

        SingleViewDeviceSessionState state;
        SingleViewDeviceSessionStateChangedEventArgs stateChanged = null!;
        lock (_gate)
        {
            if (!ReferenceEquals(_client, client) ||
                Volatile.Read(ref _intentionalStop) != 0 ||
                Volatile.Read(ref _disposed) != 0 ||
                _unexpectedExitReported != 0)
                return;

            state = _state;
            if (state != SingleViewDeviceSessionState.Starting &&
                state != SingleViewDeviceSessionState.Running)
            {
                return;
            }

            _unexpectedExitReported = 1;

            _firstFrameReceived?.TrySetException(
                new InvalidOperationException("ScrcpyNet exited before the first decoded frame was received."));

            _state = SingleViewDeviceSessionState.Failed;
            stateChanged = new(state, SingleViewDeviceSessionState.Failed);
        }

        InvokeSafely(StateChanged, stateChanged);
        if (state == SingleViewDeviceSessionState.Running)
            InvokeSafely(Exited, EventArgs.Empty);
    }

    private bool TryTransitionToRunning(
        IScrcpyNetClient expectedClient,
        out SingleViewDeviceSessionState observedState)
    {
        ArgumentNullException.ThrowIfNull(expectedClient);
        SingleViewDeviceSessionStateChangedEventArgs stateChanged = null!;

        lock (_gate)
        {
            observedState = _state;
            if (!IsValidRunningPromotion(_state, _client, expectedClient))
                return false;

            SingleViewDeviceSessionState previous = _state;
            _state = SingleViewDeviceSessionState.Running;
            observedState = _state;
            stateChanged = new(previous, _state);
        }

        InvokeSafely(StateChanged, stateChanged);
        return true;
    }

    internal static bool IsValidRunningPromotion(
        SingleViewDeviceSessionState state,
        IScrcpyNetClient? currentClient,
        IScrcpyNetClient? expectedClient)
    {
        return expectedClient is not null &&
               state == SingleViewDeviceSessionState.Starting &&
               ReferenceEquals(currentClient, expectedClient) &&
               expectedClient.Connected;
    }

    private void SetState(SingleViewDeviceSessionState state)
    {
        SingleViewDeviceSessionState previous;
        lock (_gate)
        {
            if (_state == state)
                return;
            previous = _state;
            _state = state;
        }

        SingleViewDeviceSessionStateChangedEventArgs eventArgs =
            new(previous, state);
        InvokeSafely(StateChanged, eventArgs);
    }

    private bool HasValidContentSize()
    {
        lock (_gate)
            return _contentWidth > 0 && _contentHeight > 0;
    }

    private void AddDiagnostic(string diagnostic)
    {
        lock (_gate)
            AddDiagnosticLocked(diagnostic);
    }

    private void AddDiagnosticLocked(string diagnostic)
    {
        while (_diagnostics.Count >= DiagnosticCapacity)
            _diagnostics.Dequeue();
        _diagnostics.Enqueue(diagnostic);
    }

    private IScrcpyNetClient? GetCurrentClient()
    {
        lock (_gate)
            return _client;
    }

    private IScrcpyNetClient? GetInteractiveClient()
    {
        lock (_gate)
        {
            if (!IsInteractiveClientSnapshot(
                    _state,
                    _client,
                    _disposed != 0,
                    _intentionalStop != 0))
            {
                return null;
            }

            return _client;
        }
    }

    private Task? RequireInteractiveClient(
        string operation,
        out IScrcpyNetClient? client)
    {
        client = GetInteractiveClient();
        return client is null
            ? UnavailableControlSession(operation)
            : null;
    }

    internal static bool IsInteractiveClientSnapshot(
        SingleViewDeviceSessionState state,
        IScrcpyNetClient? client,
        bool isDisposed,
        bool intentionalStop)
    {
        return !isDisposed &&
               !intentionalStop &&
               state == SingleViewDeviceSessionState.Running &&
               client is not null &&
               client.Connected;
    }

    private static Task UnavailableControlSession(string operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        return Task.FromException(
            new InvalidOperationException(
                $"The Single View control session is unavailable for {operation}."));
    }

    private static Task RejectedControlBatch(string operation)
    {
        return Task.FromException(
            new InvalidOperationException(
                $"The Single View control queue rejected the {operation} batch."));
    }

    private static Task SendCriticalBatch(
        IScrcpyNetClient client,
        IReadOnlyList<IControlMessage> messages,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentException.ThrowIfNullOrEmpty(operation);
        if (messages.Count == 0)
            throw new ArgumentException("A critical control batch cannot be empty.", nameof(messages));

        bool accepted = messages.Count == 1
            ? client.TrySendControlCommand(messages[0])
            : client.TrySendControlCommands(messages);
        return accepted
            ? Task.CompletedTask
            : RejectedControlBatch(operation);
    }

    private Task SendPanelCommandAsync(
        IControlMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? unavailable = RequireInteractiveClient(
            "panel action",
            out IScrcpyNetClient? client);
        return unavailable
            ?? SendCriticalBatch(client!, [message], "panel action");
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(ScrcpyNetSingleViewDeviceSession));
    }

    private void InvokeSafely<TEventArgs>(EventHandler<TEventArgs>? handlers, TEventArgs eventArgs)
        where TEventArgs : EventArgs
    {
        if (handlers is null)
            return;

        foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList().Cast<EventHandler<TEventArgs>>())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Single View session event handler failed for {Serial}.", Serial);
            }
        }
    }

    private void InvokeSafely(EventHandler? handlers, EventArgs eventArgs)
    {
        if (handlers is null)
            return;

        foreach (EventHandler handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Single View session event handler failed for {Serial}.", Serial);
            }
        }
    }
}
