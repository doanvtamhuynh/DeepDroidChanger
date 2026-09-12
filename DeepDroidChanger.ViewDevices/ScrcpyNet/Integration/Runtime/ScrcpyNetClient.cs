using System.Collections.Generic;
using ScrcpyNet;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetClient : IScrcpyNetClient
{
    private readonly Scrcpy _client;
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

    public ScrcpyNetClient(Scrcpy client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _client.VideoStreamDecoder.OnFrame += OnFrame;
        _client.Failed += OnFailed;
        _client.Exited += OnExited;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _client.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _client.StopAsync(cancellationToken);
    }

    public void SendControlCommand(IControlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _client.SendControlCommand(message);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _client.VideoStreamDecoder.OnFrame -= OnFrame;
        _client.Failed -= OnFailed;
        _client.Exited -= OnExited;
        _client.Dispose();
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
