using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeepDroidChanger.Views;

internal static class ViewDeviceMonitorWorkArea
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    public static Rect GetFor(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        IntPtr windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero)
            return SystemParameters.WorkArea;

        IntPtr monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        MONITORINFO monitorInfo = new()
        {
            Size = Marshal.SizeOf<MONITORINFO>()
        };
        if (monitorHandle == IntPtr.Zero || !GetMonitorInfo(monitorHandle, ref monitorInfo))
            return SystemParameters.WorkArea;

        Matrix fromDevice = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        Point topLeft = fromDevice.Transform(new Point(monitorInfo.WorkArea.Left, monitorInfo.WorkArea.Top));
        Point bottomRight = fromDevice.Transform(new Point(monitorInfo.WorkArea.Right, monitorInfo.WorkArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MONITORINFO monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
