using System.Collections.Generic;
using ScrcpyNet;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetClient : IScrcpyNetClient
{
    private readonly Scrcpy _client;
    private IDisposable? _deviceLease;
    private readonly Func<CancellationToken, Task>? _testStartAsync;
    private readonly Action? _testDisposeUnderlying;
    private readonly Task? _testServerLifecycleCompletion;
    private readonly object _diagnosticsGate = new();
    private readonly Queue<string> _diagnostics = new();
    private int _disposed;

    public Scrcpy? Client => _client;

    public bool Connected => _client.Connected;
    public int Width => _client.Width;
    public int Height => _client.Height;

    public IReadOnlyList<string> RecentDiagnostics
    {
        get
        {
            lock (_diagnosticsGate)
                return _diagnostics.ToArray();
        }
    }

    public event EventHandler<ScrcpyNetFrameEventArgs>? FrameReceived;
    public event EventHandler<ScrcpyNetErrorEventArgs>? Failed;
    public event EventHandler? Exited;
    public event EventHandler<ScrcpyClipboardChangedEventArgs>? ClipboardChanged;

    public ScrcpyNetClient(Scrcpy client)
        : this(client, null)
    {
    }

    internal ScrcpyNetClient(Scrcpy client, IDisposable? deviceLease)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _deviceLease = deviceLease;
        _client.VideoStreamDecoder.OnFrame += OnFrame;
        _client.Failed += OnFailed;
        _client.Exited += OnExited;
        _client.ClipboardChanged += OnClipboardChanged;
    }

    // Keeps lease lifecycle tests independent from the native FFmpeg decoder.
    internal ScrcpyNetClient(
        IDisposable deviceLease,
        Func<CancellationToken, Task> startAsync,
        Action disposeUnderlying,
        Task? serverLifecycleCompletion = null)
    {
        _client = null!;
        _deviceLease = deviceLease ?? throw new ArgumentNullException(nameof(deviceLease));
        _testStartAsync = startAsync ?? throw new ArgumentNullException(nameof(startAsync));
        _testDisposeUnderlying = disposeUnderlying ?? throw new ArgumentNullException(nameof(disposeUnderlying));
        _testServerLifecycleCompletion = serverLifecycleCompletion;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_testStartAsync is not null)
            return _testStartAsync(cancellationToken);

        return _client.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _client.StopAsync(cancellationToken);
    }

    public Task FlushControlAsync(CancellationToken cancellationToken = default)
    {
        return _client.FlushControlAsync(cancellationToken);
    }

    public void SendControlCommand(IControlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _client.SendControlCommand(message);
    }

    public void SendControlCommands(IReadOnlyList<IControlMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _client.SendControlCommands(messages);
    }

    public bool TrySendControlCommand(IControlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _client.TrySendControlCommands([message]);
    }

    public bool TrySendControlCommands(IReadOnlyList<IControlMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return _client.TrySendControlCommands(messages);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (_testDisposeUnderlying is not null)
            {
                _testDisposeUnderlying();
            }
            else
            {
                _client.VideoStreamDecoder.OnFrame -= OnFrame;
                _client.Failed -= OnFailed;
                _client.Exited -= OnExited;
                _client.ClipboardChanged -= OnClipboardChanged;
                _client.Dispose();
            }
        }
        finally
        {
            ReleaseDeviceLeaseWhenSafe();
        }
    }

    private void ReleaseDeviceLeaseWhenSafe()
    {
        IDisposable? lease = Interlocked.Exchange(ref _deviceLease, null);
        if (lease is null)
            return;

        Task? lifecycleCompletion = _testServerLifecycleCompletion;
        if (lifecycleCompletion is null && _testDisposeUnderlying is null)
            lifecycleCompletion = _client.ServerLifecycleCompletion;

        if (lifecycleCompletion is null || lifecycleCompletion.IsCompleted)
        {
            lease.Dispose();
            return;
        }

        _ = ReleaseDeviceLeaseAfterLifecycleAsync(lifecycleCompletion, lease);
    }

    private static async Task ReleaseDeviceLeaseAfterLifecycleAsync(
        Task lifecycleCompletion,
        IDisposable lease)
    {
        try
        {
            await lifecycleCompletion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A faulted or canceled server command is terminal as well; the
            // lease must not remain quarantined after that task completes.
        }
        finally
        {
            lease.Dispose();
        }
    }

    private void OnFrame(object? sender, FrameData frame)
    {
        if (Volatile.Read(ref _disposed) != 0 || frame.Width <= 0 || frame.Height <= 0)
            return;

        FrameReceived?.Invoke(this, new ScrcpyNetFrameEventArgs(frame.Width, frame.Height));
    }

    private void OnFailed(object? sender, ScrcpyErrorEventArgs eventArgs)
    {
        AddDiagnostic(eventArgs.Exception.ToString());
        Failed?.Invoke(this, new ScrcpyNetErrorEventArgs(eventArgs.Exception));
    }

    private void OnExited(object? sender, EventArgs eventArgs)
    {
        Exited?.Invoke(this, eventArgs);
    }

    private void OnClipboardChanged(object? sender, ScrcpyClipboardChangedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        ClipboardChanged?.Invoke(this, eventArgs);
    }

    private void AddDiagnostic(string diagnostic)
    {
        lock (_diagnosticsGate)
        {
            while (_diagnostics.Count >= 32)
                _diagnostics.Dequeue();
            _diagnostics.Enqueue(diagnostic);
        }
    }
}
