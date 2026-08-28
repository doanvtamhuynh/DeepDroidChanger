using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DeepDroidChanger.ViewDevices.Interop;

internal static class NativeWindowLocator
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SearchInterval = TimeSpan.FromMilliseconds(50);

    public static async Task<IntPtr> WaitForWindowAsync(
        Process process,
        string exactTitle,
        Func<IReadOnlyList<string>> diagnostics,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < DiscoveryTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IntPtr handle = FindTopLevelWindow(process.Id, exactTitle);
            if (handle != IntPtr.Zero)
                return handle;

            if (process.HasExited)
            {
                string detail = diagnostics().LastOrDefault() ?? "No scrcpy diagnostics were emitted.";
                throw new InvalidOperationException(
                    $"scrcpy exited with code {process.ExitCode} before creating its window. {detail}");
            }

            await Task.Delay(SearchInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out while waiting for the official scrcpy window.");
    }

    public static bool TryGetClientSize(IntPtr windowHandle, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (windowHandle == IntPtr.Zero || !GetClientRect(windowHandle, out RECT rect))
            return false;

        width = Math.Max(0, rect.Right - rect.Left);
        height = Math.Max(0, rect.Bottom - rect.Top);
        return width > 0 && height > 0;
    }

    public static void RequestClose(IntPtr windowHandle)
    {
        const uint WmClose = 0x0010;
        if (windowHandle != IntPtr.Zero && IsWindow(windowHandle))
            _ = PostMessage(windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);
    }

    private static IntPtr FindTopLevelWindow(int processId, string exactTitle)
    {
        IntPtr matched = IntPtr.Zero;
        Marshal.SetLastPInvokeError(0);
        bool completed = EnumWindows((windowHandle, _) =>
            {
                if (matched != IntPtr.Zero)
                    return false;

                GetWindowThreadProcessId(windowHandle, out uint windowProcessId);
                if (windowProcessId != processId)
                    return true;

                if (string.Equals(GetWindowTitle(windowHandle), exactTitle, StringComparison.Ordinal))
                {
                    matched = windowHandle;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        int error = Marshal.GetLastPInvokeError();
        if (!completed && matched == IntPtr.Zero && error != 0)
        {
            throw new Win32Exception(error, "Failed to enumerate scrcpy windows.");
        }

        return matched;
    }

    private static string GetWindowTitle(IntPtr windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);
        if (length <= 0)
            return string.Empty;

        StringBuilder builder = new(length + 1);
        _ = GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
