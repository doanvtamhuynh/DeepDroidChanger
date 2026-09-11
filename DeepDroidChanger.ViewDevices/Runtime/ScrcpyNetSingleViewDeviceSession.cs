using System.Collections.Generic;
using System.Linq;
using ScrcpyNet;
using SharpAdbClient;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetSingleViewDeviceSession : ISingleViewDeviceSession
{
    private const int DiagnosticCapacity = 64;

    private readonly ViewDeviceLaunchOptions _options;
    private readonly ISharpAdbDeviceResolver _deviceResolver;
    private readonly IScrcpyNetClientFactory _clientFactory;
    private readonly ILogger<ScrcpyNetSingleViewDeviceSession> _logger;
    private readonly object _gate = new();
    private readonly Queue<string> _diagnostics = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private IScrcpyNetClient? _client;
    private SingleViewDeviceSessionState _state = SingleViewDeviceSessionState.Created;
    private int _contentWidth;
    private int _contentHeight;
    private int _intentionalStop = 1;
    private int _disposed;

    public ScrcpyNetSingleViewDeviceSession(
        ViewDeviceLaunchOptions options,
        ISharpAdbDeviceResolver deviceResolver,
        IScrcpyNetClientFactory clientFactory,
        ILogger<ScrcpyNetSingleViewDeviceSession> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(deviceResolver);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _deviceResolver = deviceResolver;
        _clientFactory = clientFactory;
        _logger = logger;
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
                DeviceData device = await Task.Run(
                        () => _deviceResolver.Resolve(Serial),
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                IScrcpyNetClient client = _clientFactory.Create(device, _options);
                lock (_gate)
                {
                    _client = client;
                    Volatile.Write(ref _intentionalStop, 0);
                }
                SubscribeToClient(client);

                try
                {
                    await client.StartAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!client.Connected)
                        throw new InvalidOperationException("ScrcpyNet did not report a connected session after start.");
                }
                catch
                {
                    await CleanupClientAsync().ConfigureAwait(false);
                    throw;
                }

                SetContentSize(client.Width, client.Height);
                if (State != SingleViewDeviceSessionState.Starting)
                {
                    if (State == SingleViewDeviceSessionState.Failed)
                    {
                        throw new InvalidOperationException(
                            "The ScrcpyNet client exited while the session was starting.");
                    }

                    throw new InvalidOperationException(
                        $"The session entered an unexpected state ({State}) while starting.");
                }

                SetState(SingleViewDeviceSessionState.Running);
            }
            catch (OperationCanceledException)
            {
                await CleanupClientAsync().ConfigureAwait(false);
                SetState(SingleViewDeviceSessionState.Closed);
                throw;
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

    public Task SendKeyEventAsync(int keyCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IScrcpyNetClient? client = GetInteractiveClient();
        if (client is null)
            return Task.CompletedTask;

        KeycodeControlMessage keyDown = new()
        {
            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN,
            KeyCode = (AndroidKeycode)keyCode
        };
        KeycodeControlMessage keyUp = new()
        {
            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
            KeyCode = (AndroidKeycode)keyCode
        };
        client.SendControlCommand(keyDown);
        client.SendControlCommand(keyUp);
        return Task.CompletedTask;
    }

    public Task SendBackOrScreenOnAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IScrcpyNetClient? client = GetInteractiveClient();
        if (client is null)
            return Task.CompletedTask;

        client.SendControlCommand(new BackOrScreenOnControlMessage
        {
            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN
        });
        client.SendControlCommand(new BackOrScreenOnControlMessage
        {
            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP
        });
        return Task.CompletedTask;
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
    }

    private void UnsubscribeFromClient(IScrcpyNetClient client)
    {
        client.FrameReceived -= OnClientFrameReceived;
        client.Failed -= OnClientFailed;
        client.Exited -= OnClientExited;
    }

    private void OnClientFrameReceived(object? sender, ScrcpyNetFrameEventArgs eventArgs)
    {
        if (sender is IScrcpyNetClient client && IsCurrentClient(client))
            SetContentSize(eventArgs.Width, eventArgs.Height);
    }

    private void OnClientFailed(object? sender, ScrcpyNetErrorEventArgs eventArgs)
    {
        if (sender is IScrcpyNetClient client && IsCurrentClient(client))
            AddDiagnostic(eventArgs.Exception.ToString());
    }

    private void OnClientExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not IScrcpyNetClient client ||
            !IsCurrentClient(client) ||
            Volatile.Read(ref _intentionalStop) != 0 ||
            Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        SetState(SingleViewDeviceSessionState.Failed);
        InvokeSafely(Exited, EventArgs.Empty);
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

    private void SetContentSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        lock (_gate)
        {
            if (_contentWidth == width && _contentHeight == height)
                return;
            _contentWidth = width;
            _contentHeight = height;
        }

        InvokeSafely(ContentSizeChanged, new SingleViewDeviceContentSizeChangedEventArgs(width, height));
    }

    private void AddDiagnostic(string diagnostic)
    {
        lock (_gate)
        {
            while (_diagnostics.Count >= DiagnosticCapacity)
                _diagnostics.Dequeue();
            _diagnostics.Enqueue(diagnostic);
        }
    }

    private IScrcpyNetClient? GetCurrentClient()
    {
        lock (_gate)
            return _client;
    }

    private IScrcpyNetClient? GetInteractiveClient()
    {
        if (State != SingleViewDeviceSessionState.Running ||
            Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        return GetCurrentClient();
    }

    private bool IsCurrentClient(IScrcpyNetClient client)
    {
        lock (_gate)
            return ReferenceEquals(_client, client);
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
