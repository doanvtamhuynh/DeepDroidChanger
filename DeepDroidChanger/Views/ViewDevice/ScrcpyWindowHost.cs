using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DeepDroidChanger.Views;

public sealed class ScrcpyWindowHost : HwndHost
{
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsClipChildren = 0x02000000L;
    private const long WsClipSiblings = 0x04000000L;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const uint WmSize = 0x0005;
    private const uint WmParentNotify = 0x0210;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmRightButtonDown = 0x0204;
    private const uint WmMiddleButtonDown = 0x0207;
    private const uint WmXButtonDown = 0x020B;
    private const uint WmPointerDown = 0x0246;
    private IntPtr _containerHandle;
    private IntPtr _scrcpyHandle;
    private IntPtr _requestedHandle;
    private IntPtr _originalParent;
    private IntPtr _originalStyle;
    private int _lastChildWidth = -1;
    private int _lastChildHeight = -1;

    public IntPtr AttachedWindowHandle => _scrcpyHandle;

    public void AttachWindow(IntPtr windowHandle)
    {
        Dispatcher.VerifyAccess();
        _requestedHandle = windowHandle;
        if (_scrcpyHandle == windowHandle &&
            windowHandle != IntPtr.Zero &&
            IsWindow(windowHandle) &&
            GetParent(windowHandle) == _containerHandle)
        {
            ResizeChild();
            return;
        }

        DetachWindowCore();
        if (windowHandle == IntPtr.Zero || _containerHandle == IntPtr.Zero)
            return;
        if (!IsWindow(windowHandle))
        {
            _requestedHandle = IntPtr.Zero;
            throw new Win32Exception("The official scrcpy window disappeared before it could be embedded.");
        }

        _scrcpyHandle = windowHandle;
        _originalParent = GetParent(windowHandle);
        _originalStyle = GetWindowLongPtr(windowHandle, GwlStyle);
        _ = ShowWindow(windowHandle, SwHide);
        try
        {
            long style = _originalStyle.ToInt64();
            style &= ~(WsPopup | WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu);
            style |= WsChild | WsVisible | WsClipChildren | WsClipSiblings;
            SetWindowLongPtrChecked(windowHandle, GwlStyle, new IntPtr(style));

            Marshal.SetLastPInvokeError(0);
            IntPtr previousParent = SetParent(windowHandle, _containerHandle);
            int error = Marshal.GetLastPInvokeError();
            if (previousParent == IntPtr.Zero && error != 0)
                throw new Win32Exception(error, "Failed to embed the official scrcpy window.");

            IntPtr embeddedParent = GetParent(windowHandle);
            for (int attempt = 0; embeddedParent != _containerHandle && attempt < 3; attempt++)
            {
                Marshal.SetLastPInvokeError(0);
                _ = SetParent(windowHandle, _containerHandle);
                error = Marshal.GetLastPInvokeError();
                if (error != 0)
                    throw new Win32Exception(error, "Failed to retain the official scrcpy child-window parent.");
                embeddedParent = GetParent(windowHandle);
            }

            long embeddedStyle = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
            if (embeddedParent != _containerHandle || (embeddedStyle & WsChild) == 0)
            {
                throw new Win32Exception(
                    $"The official scrcpy window did not become a verified child window. " +
                    $"ExpectedParent=0x{_containerHandle.ToInt64():X}, " +
                    $"ActualParent=0x{embeddedParent.ToInt64():X}, " +
                    $"PreviousParent=0x{previousParent.ToInt64():X}, " +
                    $"OriginalParent=0x{_originalParent.ToInt64():X}, " +
                    $"Style=0x{embeddedStyle:X}.");
            }

            if (!ResizeChild(frameChanged: true))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Failed to size the embedded official scrcpy window.");
            }
            _ = ShowWindow(windowHandle, SwShow);
        }
        catch
        {
            if (IsWindow(windowHandle))
                _ = ShowWindow(windowHandle, SwHide);
            _requestedHandle = IntPtr.Zero;
            ResetAttachedWindowState();
            throw;
        }
    }

    public void DetachWindow()
    {
        Dispatcher.VerifyAccess();
        _requestedHandle = IntPtr.Zero;
        DetachWindowCore();
    }

    private void DetachWindowCore()
    {
        IntPtr windowHandle = _scrcpyHandle;
        if (windowHandle != IntPtr.Zero && IsWindow(windowHandle))
            _ = ShowWindow(windowHandle, SwHide);

        ResetAttachedWindowState();
    }

    private void ResetAttachedWindowState()
    {
        _scrcpyHandle = IntPtr.Zero;
        _originalParent = IntPtr.Zero;
        _originalStyle = IntPtr.Zero;
        _lastChildWidth = -1;
        _lastChildHeight = -1;
    }

    public void FocusNativeWindow()
    {
        Dispatcher.VerifyAccess();
        IntPtr windowHandle = _scrcpyHandle;
        if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            return;

        uint currentThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(windowHandle, out _);
        if (currentThread == 0 || targetThread == 0)
            return;

        bool inputAttached = false;
        try
        {
            // SetParent already joins the input queues for a cross-thread parent/child
            // relationship. Attaching and then detaching that pair would tear down the
            // relationship which routes physical keyboard input to the embedded window.
            bool hasEmbeddedInputRelationship = GetParent(windowHandle) == _containerHandle;
            if (currentThread != targetThread &&
                !hasEmbeddedInputRelationship)
            {
                inputAttached = AttachThreadInput(currentThread, targetThread, attach: true);
                if (!inputAttached)
                {
                    Debug.WriteLine(
                        $"Failed to attach the WPF and scrcpy input queues. Error={Marshal.GetLastPInvokeError()}.");
                    return;
                }
            }

            if (!IsWindow(windowHandle))
                return;

            Marshal.SetLastPInvokeError(0);
            IntPtr previousFocus = SetFocus(windowHandle);
            int error = Marshal.GetLastPInvokeError();
            if (previousFocus == IntPtr.Zero && error != 0)
                Debug.WriteLine($"Failed to focus the embedded scrcpy window. Error={error}.");
        }
        finally
        {
            if (inputAttached && !AttachThreadInput(currentThread, targetThread, attach: false))
            {
                Debug.WriteLine(
                    $"Failed to detach the WPF and scrcpy input queues. Error={Marshal.GetLastPInvokeError()}.");
            }
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        const uint hostStyle = (uint)(WsChild | WsVisible | WsClipChildren | WsClipSiblings);
        _containerHandle = CreateWindowEx(
            0,
            "static",
            string.Empty,
            hostStyle,
            0,
            0,
            1,
            1,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (_containerHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create the scrcpy native host.");

        if (_requestedHandle != IntPtr.Zero)
            Dispatcher.BeginInvoke(() => AttachWindow(_requestedHandle));

        return new HandleRef(this, _containerHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _requestedHandle = IntPtr.Zero;
        DetachWindowCore();
        if (hwnd.Handle != IntPtr.Zero)
            _ = DestroyWindow(hwnd.Handle);
        _containerHandle = IntPtr.Zero;
    }

    protected override IntPtr WndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if ((uint)message == WmSize)
            ResizeChild();
        else if ((uint)message == WmParentNotify && IsPointerDownMessage(LowWord(wParam)))
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusNativeWindow));
        return IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeChild();
    }

    protected override bool TabIntoCore(TraversalRequest request)
    {
        FocusNativeWindow();
        return _scrcpyHandle != IntPtr.Zero;
    }

    private bool ResizeChild(bool frameChanged = false)
    {
        if (_containerHandle == IntPtr.Zero ||
            _scrcpyHandle == IntPtr.Zero ||
            !IsWindow(_scrcpyHandle) ||
            !GetClientRect(_containerHandle, out RECT rect))
        {
            return false;
        }

        int width = Math.Max(1, rect.Right - rect.Left);
        int height = Math.Max(1, rect.Bottom - rect.Top);
        if (!frameChanged && width == _lastChildWidth && height == _lastChildHeight)
            return true;

        uint flags = SwpNoZOrder | SwpNoActivate;
        if (frameChanged)
            flags |= SwpFrameChanged;

        if (SetWindowPos(
            _scrcpyHandle,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            flags))
        {
            _lastChildWidth = width;
            _lastChildHeight = height;
            return true;
        }

        return false;
    }

    private static void SetWindowLongPtrChecked(IntPtr windowHandle, int index, IntPtr value)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr result = SetWindowLongPtr(windowHandle, index, value);
        int error = Marshal.GetLastPInvokeError();
        if (result == IntPtr.Zero && error != 0)
            throw new Win32Exception(error, "Failed to configure the official scrcpy child window.");
    }

    private static IntPtr GetWindowLongPtr(IntPtr windowHandle, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : new IntPtr(GetWindowLong32(windowHandle, index));
    }

    private static IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : new IntPtr(SetWindowLong32(windowHandle, index, value.ToInt32()));
    }

    private static uint LowWord(IntPtr value)
    {
        return unchecked((ushort)value.ToInt64());
    }

    private static bool IsPointerDownMessage(uint message)
    {
        return message is WmLeftButtonDown or
            WmRightButtonDown or
            WmMiddleButtonDown or
            WmXButtonDown or
            WmPointerDown;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr windowHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr windowHandle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
