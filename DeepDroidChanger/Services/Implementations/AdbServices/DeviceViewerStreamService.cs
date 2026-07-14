using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceViewerStreamService : IDeviceViewerStreamService, IDisposable
    {
        private const int WindowDiscoveryTimeoutMilliseconds = 10000;
        private const int WindowSearchDelayMilliseconds = 75;
        private const int GracefulStopTimeoutMilliseconds = 1000;
        private const int StopTimeoutMilliseconds = 3000;
        private const int GwlHwndParent = -8;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNA = 8;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_HIDEWINDOW = 0x0080;
        private static readonly IntPtr HwndTop = IntPtr.Zero;
        private readonly ConcurrentDictionary<string, DeviceViewerStreamSession> _activeStreamSessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _serialLocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _lifetimeCancellation = new();

        private readonly ILogger<DeviceViewerStreamService> _logger;
        private bool _disposed;

        public DeviceViewerStreamService(ILogger<DeviceViewerStreamService> logger)
        {
            _logger = logger;
        }

        public async Task<IDeviceViewerStreamSession> StartAsync(
            string serial,
            IntPtr ownerHwnd,
            DeviceViewerStreamBounds bounds,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
            CancellationToken effectiveCancellationToken = linkedCancellation.Token;

            if (string.IsNullOrWhiteSpace(serial))
                throw new ArgumentException("Device serial is required.", nameof(serial));

            if (ownerHwnd == IntPtr.Zero)
                throw new InvalidOperationException("Device viewer owner window handle is not available.");

            if (!bounds.IsValid())
                throw new InvalidOperationException($"Invalid device viewer stream bounds: {bounds.Width}x{bounds.Height}.");

            var serialLock = _serialLocks.GetOrAdd(serial, _ => new SemaphoreSlim(1, 1));
            await serialLock.WaitAsync(effectiveCancellationToken).ConfigureAwait(false);

            try
            {
                if (_activeStreamSessions.TryRemove(serial, out var existingSession))
                {
                    await existingSession.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    existingSession.Dispose();
                }

                var session = await StartCoreAsync(serial, ownerHwnd, bounds, effectiveCancellationToken).ConfigureAwait(false);
                if (_disposed)
                {
                    await session.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    session.Dispose();
                    throw new ObjectDisposedException(nameof(DeviceViewerStreamService));
                }

                _activeStreamSessions[serial] = session;
                return session;
            }
            finally
            {
                serialLock.Release();
            }
        }

        private async Task<DeviceViewerStreamSession> StartCoreAsync(
            string serial,
            IntPtr ownerHwnd,
            DeviceViewerStreamBounds bounds,
            CancellationToken cancellationToken)
        {
            var toolPath = await ResolveToolPathAsync(cancellationToken).ConfigureAwait(false);
            var platformToolsPath = Path.GetDirectoryName(toolPath) ?? AppContext.BaseDirectory;
            var windowTitle = CreateWindowTitle(serial);

            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                WorkingDirectory = platformToolsPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            AddScrcpyArguments(startInfo, serial, windowTitle);

            _logger.LogDebug(
                "Starting external scrcpy for {Serial}.",
                serial);

            var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Failed to start scrcpy process.");

            var startupErrors = new ConcurrentQueue<string>();
            process.ErrorDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data))
                    return;

                startupErrors.Enqueue(args.Data);
                _logger.LogDebug(
                    "scrcpy emitted a diagnostic line for {Serial}. LineLength: {LineLength}",
                    serial,
                    args.Data.Length);
            };
            process.BeginErrorReadLine();

            try
            {
                var streamHwnd = await FindStreamWindowAsync(process, windowTitle, startupErrors, cancellationToken)
                    .ConfigureAwait(false);

                ConfigureExternalWindow(streamHwnd, ownerHwnd);

                var session = new DeviceViewerStreamSession(serial, process, streamHwnd, _logger, RemoveActiveSession);
                session.UpdateBounds(bounds);
                session.SetVisible(false);
                return session;
            }
            catch
            {
                await StopProcessAsync(process, _logger, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        private static void AddScrcpyArguments(ProcessStartInfo startInfo, string serial, string windowTitle)
        {
            startInfo.ArgumentList.Add(AdbToolConstants.ScrcpySerialArgument);
            startInfo.ArgumentList.Add(serial);
            startInfo.ArgumentList.Add(AdbToolConstants.ScrcpyWindowTitleArgument);
            startInfo.ArgumentList.Add(windowTitle);
            startInfo.ArgumentList.Add(AdbToolConstants.ScrcpyWindowBorderlessArgument);
            startInfo.ArgumentList.Add(AdbToolConstants.ScrcpyNoAudioArgument);
        }

        private async Task<IntPtr> FindStreamWindowAsync(
            Process process,
            string windowTitle,
            ConcurrentQueue<string> startupErrors,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < WindowDiscoveryTimeoutMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hwnd = FindTopLevelWindow(process.Id, windowTitle);
                if (hwnd != IntPtr.Zero)
                    return hwnd;

                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        startupErrors.IsEmpty
                            ? "scrcpy process exited unexpectedly before creating a window."
                            : "scrcpy process exited unexpectedly before creating a window after emitting diagnostics.");
                }

                await Task.Delay(WindowSearchDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException("Timeout waiting for device viewer stream window.");
        }

        private static IntPtr FindTopLevelWindow(int processId, string windowTitle)
        {
            var matchedHwnd = IntPtr.Zero;

            EnumWindows((hwnd, _) =>
            {
                if (matchedHwnd != IntPtr.Zero)
                    return false;

                GetWindowThreadProcessId(hwnd, out var windowProcessId);
                if (windowProcessId != processId)
                    return true;

                var title = GetWindowTitle(hwnd);
                if (string.Equals(title, windowTitle, StringComparison.Ordinal))
                {
                    matchedHwnd = hwnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return matchedHwnd;
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
                return string.Empty;

            var builder = new StringBuilder(length + 1);
            _ = GetWindowText(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }

        private static void ConfigureExternalWindow(IntPtr streamHwnd, IntPtr ownerHwnd)
        {
            _ = SetWindowLongPtr(streamHwnd, GwlHwndParent, ownerHwnd);
        }

        private static string CreateWindowTitle(string serial)
        {
            return $"{AdbToolConstants.ScrcpyWindowTitlePrefix}-{serial}-{Guid.NewGuid():N}";
        }

        private static async Task<string> ResolveToolPathAsync(CancellationToken cancellationToken)
        {
            var outputPath = Path.Combine(
                AppContext.BaseDirectory,
                AdbToolConstants.ToolsRootRelativePath,
                AdbToolConstants.PlatformToolsDirectoryName,
                AdbToolConstants.ScrcpyExecutableName);
            var projectPath = Path.Combine(
                Environment.CurrentDirectory,
                AdbToolConstants.ToolsRootRelativePath,
                AdbToolConstants.PlatformToolsDirectoryName,
                AdbToolConstants.ScrcpyExecutableName);

            if (await FileExistsAsync(outputPath, cancellationToken).ConfigureAwait(false))
                return outputPath;

            if (await FileExistsAsync(projectPath, cancellationToken).ConfigureAwait(false))
                return projectPath;

            return AdbToolConstants.ScrcpyExecutableName;
        }

        private static Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
        {
            return Task.Run(() => File.Exists(path), cancellationToken);
        }

        private static async Task StopProcessAsync(Process process, ILogger logger, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!process.HasExited)
                {
                    var closeRequested = false;
                    try
                    {
                        closeRequested = process.CloseMainWindow();
                    }
                    catch (InvalidOperationException)
                    {
                        closeRequested = false;
                    }

                    if (closeRequested)
                    {
                        try
                        {
                            using var gracefulCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            gracefulCts.CancelAfter(GracefulStopTimeoutMilliseconds);
                            await process.WaitForExitAsync(gracefulCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            logger.LogDebug("Timeout waiting for scrcpy process to close gracefully.");
                        }
                    }

                    if (!process.HasExited)
                        process.Kill(true);

                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(StopTimeoutMilliseconds);
                        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogWarning("Timeout waiting for scrcpy process to exit. Forcing kill again.");
                        if (!process.HasExited)
                            process.Kill(true);
                    }
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to stop scrcpy process.");
            }
            finally
            {
                process.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lifetimeCancellation.Cancel();
            foreach (DeviceViewerStreamSession session in _activeStreamSessions.Values.ToArray())
                session.Dispose();

            _activeStreamSessions.Clear();
            _serialLocks.Clear();
            _lifetimeCancellation.Dispose();
        }

        private void RemoveActiveSession(string serial, DeviceViewerStreamSession session)
        {
            if (_activeStreamSessions.TryGetValue(serial, out var activeSession) && ReferenceEquals(activeSession, session))
                _activeStreamSessions.TryRemove(serial, out _);
        }

        private sealed class DeviceViewerStreamSession : IDeviceViewerStreamSession
        {
            private readonly string _serial;
            private readonly Process _process;
            private readonly IntPtr _hwnd;
            private readonly ILogger _logger;
            private readonly Action<string, DeviceViewerStreamSession> _removeActiveSession;
            private readonly SemaphoreSlim _stopLock = new(1, 1);
            private DeviceViewerStreamBounds _lastBounds;
            private bool _isVisible;
            private bool _stopped;
            private bool _disposed;

            public event EventHandler? Exited;

            public DeviceViewerStreamSession(
                string serial,
                Process process,
                IntPtr hwnd,
                ILogger logger,
                Action<string, DeviceViewerStreamSession> removeActiveSession)
            {
                _serial = serial;
                _process = process;
                _hwnd = hwnd;
                _logger = logger;
                _removeActiveSession = removeActiveSession;
                _process.EnableRaisingEvents = true;
                _process.Exited += OnProcessExited;
            }

            public bool HasExited
            {
                get
                {
                    if (_stopped || _disposed)
                        return true;

                    try
                    {
                        return _process.HasExited;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                }
            }

            public void UpdateBounds(DeviceViewerStreamBounds bounds)
            {
                if (_stopped || _disposed || _hwnd == IntPtr.Zero || !IsWindow(_hwnd))
                    return;

                _lastBounds = bounds;
                if (!bounds.IsValid())
                {
                    SetVisible(false);
                    return;
                }

                var flags = SWP_NOZORDER | SWP_NOACTIVATE | (_isVisible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW);
                if (!SetWindowPos(_hwnd, HwndTop, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags))
                {
                    _logger.LogWarning(
                        "Failed to synchronize external stream window for {Serial}. LastError: {LastError}",
                        _serial,
                        Marshal.GetLastWin32Error());
                }
            }

            public void SetVisible(bool isVisible)
            {
                if (_stopped || _disposed || _hwnd == IntPtr.Zero || !IsWindow(_hwnd))
                    return;

                _isVisible = isVisible;
                if (isVisible && _lastBounds.IsValid())
                {
                    UpdateBounds(_lastBounds);
                    ShowWindow(_hwnd, SW_SHOWNA);
                    return;
                }

                ShowWindow(_hwnd, SW_HIDE);
            }

            public void Activate()
            {
                if (_stopped || _disposed || _hwnd == IntPtr.Zero || !IsWindow(_hwnd))
                    return;

                SetVisible(true);

                var currentThreadId = GetCurrentThreadId();
                var targetThreadId = GetWindowThreadProcessId(_hwnd, out _);
                var attached = targetThreadId != 0 &&
                    targetThreadId != currentThreadId &&
                    AttachThreadInput(currentThreadId, targetThreadId, true);

                try
                {
                    BringWindowToTop(_hwnd);
                    SetForegroundWindow(_hwnd);
                    SetActiveWindow(_hwnd);
                    SetFocus(_hwnd);
                }
                finally
                {
                    if (attached)
                        AttachThreadInput(currentThreadId, targetThreadId, false);
                }
            }

            public async Task StopAsync(CancellationToken cancellationToken = default)
            {
                await _stopLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    if (_stopped)
                        return;

                    _stopped = true;
                    _process.Exited -= OnProcessExited;
                    if (_hwnd != IntPtr.Zero)
                        ShowWindow(_hwnd, SW_HIDE);
                    _removeActiveSession(_serial, this);
                    await StopProcessAsync(_process, _logger, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _stopLock.Release();
                }
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                _stopLock.Dispose();
                _disposed = true;
            }

            private void OnProcessExited(object? sender, EventArgs e)
            {
                if (_stopped || _disposed)
                    return;

                _logger.LogInformation("scrcpy process exited for {Serial}.", _serial);
                _removeActiveSession(_serial, this);
                Exited?.Invoke(this, EventArgs.Empty);
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "EnumWindows", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextLength", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowText", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", EntryPoint = "ShowWindow")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "BringWindowToTop")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SetActiveWindow")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SetFocus")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "AttachThreadInput", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
        private static extern uint GetCurrentThreadId();
    }
}
