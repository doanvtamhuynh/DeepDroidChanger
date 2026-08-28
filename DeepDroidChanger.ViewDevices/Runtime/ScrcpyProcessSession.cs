using System.Diagnostics;
using System.Text.RegularExpressions;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Interop;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Runtime;

internal sealed class ScrcpyProcessSession : IViewDeviceSession
{
    private const int DiagnosticCapacity = 64;
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ForcedStopTimeout = TimeSpan.FromSeconds(3);
    private static readonly Regex TextureSizePattern = new(
        @"Texture:\s*(?<width>\d+)x(?<height>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly object _stateGate = new();
    private readonly Queue<string> _diagnostics = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ViewDeviceLaunchOptions _options;
    private readonly ScrcpyRuntimeInfo _runtime;
    private readonly ProcessJob _processJob;
    private Process? _process;
    private ViewDeviceSessionState _state = ViewDeviceSessionState.Created;
    private IntPtr _nativeWindowHandle;
    private int _contentWidth;
    private int _contentHeight;
    private int _intentionalStop;
    private int _startupCompleted;
    private int _startupExitObserved;
    private int _disposed;

    public ScrcpyProcessSession(
        ViewDeviceLaunchOptions options,
        ScrcpyRuntimeInfo runtime,
        ProcessJob processJob)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Serial);
        _options = options;
        _runtime = runtime;
        _processJob = processJob;
    }

    public string Serial => _options.Serial;

    public ViewDeviceSessionState State
    {
        get
        {
            lock (_stateGate)
                return _state;
        }
    }

    public IntPtr NativeWindowHandle
    {
        get
        {
            lock (_stateGate)
                return _nativeWindowHandle;
        }
    }

    public int ContentWidth
    {
        get
        {
            lock (_stateGate)
                return _contentWidth;
        }
    }

    public int ContentHeight
    {
        get
        {
            lock (_stateGate)
                return _contentHeight;
        }
    }

    public IReadOnlyList<string> RecentDiagnostics
    {
        get
        {
            lock (_diagnostics)
                return _diagnostics.ToArray();
        }
    }

    public event EventHandler<ViewDeviceSessionStateChangedEventArgs>? StateChanged;
    public event EventHandler? NativeWindowReady;
    public event EventHandler<ViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
    public event EventHandler? Exited;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == ViewDeviceSessionState.Running)
                return;
            if (_process is not null)
                throw new InvalidOperationException("The scrcpy session has already been started.");

            SetState(ViewDeviceSessionState.Starting);
            Volatile.Write(ref _intentionalStop, 0);
            Volatile.Write(ref _startupCompleted, 0);
            Volatile.Write(ref _startupExitObserved, 0);
            string windowTitle = $"DeepDroidChanger.ViewDevice.{Serial}.{Guid.NewGuid():N}";
            Process process = new()
            {
                StartInfo = CreateStartInfo(_options, _runtime, windowTitle),
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += OnDiagnosticDataReceived;
            process.ErrorDataReceived += OnDiagnosticDataReceived;
            process.Exited += OnProcessExited;
            _process = process;

            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("Failed to start the official scrcpy client.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _ = _processJob.TryAssign(process);

                IntPtr windowHandle = await NativeWindowLocator.WaitForWindowAsync(
                        process,
                        windowTitle,
                        () => RecentDiagnostics,
                        cancellationToken)
                    .ConfigureAwait(false);

                lock (_stateGate)
                    _nativeWindowHandle = windowHandle;

                if (NativeWindowLocator.TryGetClientSize(windowHandle, out int width, out int height))
                    SetContentSize(width, height);

                if (process.HasExited || Volatile.Read(ref _startupExitObserved) != 0)
                    throw new InvalidOperationException("scrcpy exited while its native window was starting.");

                NativeWindowReady?.Invoke(this, EventArgs.Empty);
                SetState(ViewDeviceSessionState.Running);
                Volatile.Write(ref _startupCompleted, 1);
                if (Volatile.Read(ref _startupExitObserved) != 0)
                    throw new InvalidOperationException("scrcpy exited while its native window was starting.");
            }
            catch (OperationCanceledException)
            {
                Volatile.Write(ref _intentionalStop, 1);
                await StopProcessCoreAsync(process, CancellationToken.None).ConfigureAwait(false);
                ClearProcess(process);
                SetState(ViewDeviceSessionState.Closed);
                throw;
            }
            catch
            {
                Volatile.Write(ref _intentionalStop, 1);
                await StopProcessCoreAsync(process, CancellationToken.None).ConfigureAwait(false);
                ClearProcess(process);
                SetState(ViewDeviceSessionState.Failed);
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
            Process? process = _process;
            if (process is null)
            {
                if (State != ViewDeviceSessionState.Closed)
                    SetState(ViewDeviceSessionState.Closed);
                return;
            }

            Volatile.Write(ref _intentionalStop, 1);
            SetState(ViewDeviceSessionState.Closing);

            IntPtr windowHandle;
            lock (_stateGate)
            {
                windowHandle = _nativeWindowHandle;
                _nativeWindowHandle = IntPtr.Zero;
            }

            NativeWindowLocator.RequestClose(windowHandle);
            await StopProcessCoreAsync(process, cancellationToken).ConfigureAwait(false);
            ClearProcess(process);
            SetState(ViewDeviceSessionState.Closed);
        }
        finally
        {
            _lifecycleGate.Release();
        }
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

    internal static ProcessStartInfo CreateStartInfo(
        ViewDeviceLaunchOptions options,
        ScrcpyRuntimeInfo runtime,
        string windowTitle)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = runtime.ExecutablePath,
            WorkingDirectory = runtime.RuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.Environment["ADB"] = runtime.CanonicalAdbPath;
        startInfo.Environment["SCRCPY_SERVER_PATH"] = runtime.ServerPath;
        startInfo.ArgumentList.Add("--serial");
        startInfo.ArgumentList.Add(options.Serial);
        startInfo.ArgumentList.Add("--window-title");
        startInfo.ArgumentList.Add(windowTitle);
        startInfo.ArgumentList.Add("--window-x=-32000");
        startInfo.ArgumentList.Add("--window-y=-32000");
        startInfo.ArgumentList.Add("--window-borderless");
        startInfo.ArgumentList.Add("--video-codec=h264");
        startInfo.ArgumentList.Add($"--max-size={options.MaxSize}");
        startInfo.ArgumentList.Add($"--max-fps={options.MaxFps}");
        startInfo.ArgumentList.Add($"--video-bit-rate={options.VideoBitRate}");

        return startInfo;
    }

    private void OnDiagnosticDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        string? line = eventArgs.Data;
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_diagnostics)
        {
            while (_diagnostics.Count >= DiagnosticCapacity)
                _diagnostics.Dequeue();
            _diagnostics.Enqueue(line);
        }

        if (TryParseTextureSize(line, out int width, out int height))
            SetContentSize(width, height);
    }

    internal static bool TryParseTextureSize(string? line, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        Match match = TextureSizePattern.Match(line);
        if (!match.Success ||
            !int.TryParse(match.Groups["width"].Value, out int parsedWidth) ||
            !int.TryParse(match.Groups["height"].Value, out int parsedHeight) ||
            parsedWidth <= 0 ||
            parsedHeight <= 0)
        {
            return false;
        }

        width = parsedWidth;
        height = parsedHeight;
        return true;
    }

    private void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _intentionalStop) != 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Volatile.Read(ref _startupCompleted) == 0)
        {
            Volatile.Write(ref _startupExitObserved, 1);
            return;
        }

        lock (_stateGate)
            _nativeWindowHandle = IntPtr.Zero;
        SetState(ViewDeviceSessionState.Failed);
        Exited?.Invoke(this, EventArgs.Empty);
    }

    private void SetContentSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        lock (_stateGate)
        {
            if (_contentWidth == width && _contentHeight == height)
                return;
            _contentWidth = width;
            _contentHeight = height;
        }

        ContentSizeChanged?.Invoke(this, new ViewDeviceContentSizeChangedEventArgs(width, height));
    }

    private void SetState(ViewDeviceSessionState state)
    {
        ViewDeviceSessionState previous;
        lock (_stateGate)
        {
            if (_state == state)
                return;
            previous = _state;
            _state = state;
        }

        StateChanged?.Invoke(this, new ViewDeviceSessionStateChangedEventArgs(previous, state));
    }

    private void ClearProcess(Process process)
    {
        process.Exited -= OnProcessExited;
        process.OutputDataReceived -= OnDiagnosticDataReceived;
        process.ErrorDataReceived -= OnDiagnosticDataReceived;
        process.Dispose();
        if (ReferenceEquals(_process, process))
            _process = null;
    }

    private static async Task StopProcessCoreAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
                return;

            if (await WaitForExitAsync(process, GracefulStopTimeout, cancellationToken).ConfigureAwait(false))
                return;

            process.Kill(entireProcessTree: true);
            if (!await WaitForExitAsync(process, ForcedStopTimeout, CancellationToken.None).ConfigureAwait(false))
                throw new TimeoutException("The official scrcpy process did not exit after it was killed.");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (process.HasExited)
            return true;

        using CancellationTokenSource timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
