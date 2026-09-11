using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DeepDroidChanger.Helpers;

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
    private Rect? _requestedVisibleClip;
    private IntPtr _originalParent;
    private IntPtr _originalStyle;
    private bool _originalStyleCaptured;
    private int _lastChildWidth = -1;
    private int _lastChildHeight = -1;

    public IntPtr AttachedWindowHandle => _scrcpyHandle;

    public event Action<IntPtr>? AttachSucceeded;

    public event Action<Exception, IntPtr>? AttachFailed;

    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.Register(
            nameof(IsInteractive),
            typeof(bool),
            typeof(ScrcpyWindowHost),
            new FrameworkPropertyMetadata(true));

    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public void AttachWindow(IntPtr windowHandle)
    {
        Dispatcher.VerifyAccess();
        _requestedHandle = windowHandle;
        if (_scrcpyHandle == windowHandle &&
            IsVerifiedEmbeddedWindow(windowHandle))
        {
            if (!ResizeChild())
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Failed to resize the existing embedded scrcpy window.");
            }

            ApplyVisibleClipSafely();
            ReportAttachSucceeded(windowHandle);
            return;
        }

        DetachWindowCore();
        if (windowHandle == IntPtr.Zero || _containerHandle == IntPtr.Zero)
            return;
        if (!IsWindow(_containerHandle))
        {
            _requestedHandle = IntPtr.Zero;
            throw new InvalidOperationException("The scrcpy native host window is no longer valid.");
        }
        if (!IsWindow(windowHandle))
        {
            _requestedHandle = IntPtr.Zero;
            throw new Win32Exception("The official scrcpy window disappeared before it could be embedded.");
        }

        _scrcpyHandle = windowHandle;
        _originalParent = GetParent(windowHandle);
        Marshal.SetLastPInvokeError(0);
        _originalStyle = GetWindowLongPtr(windowHandle, GwlStyle);
        int styleError = Marshal.GetLastPInvokeError();
        if (_originalStyle == IntPtr.Zero && styleError != 0)
        {
            _requestedHandle = IntPtr.Zero;
            ResetAttachedWindowState();
            throw new Win32Exception(styleError, "Failed to read the official scrcpy window style.");
        }

        _originalStyleCaptured = true;
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
            if (!IsVerifiedEmbeddedWindow(windowHandle))
            {
                throw new Win32Exception(
                    "The official scrcpy window was no longer a verified child window after attaching.");
            }

            ApplyVisibleClipSafely();
            ReportAttachSucceeded(windowHandle);
        }
        catch
        {
            DetachWindowCore();
            _requestedHandle = IntPtr.Zero;
            throw;
        }
    }

    public void SetVisibleClip(Rect? visibleRect)
    {
        Dispatcher.VerifyAccess();
        _requestedVisibleClip = visibleRect;
        if (_containerHandle == IntPtr.Zero || !IsWindow(_containerHandle))
            return;

        ApplyVisibleClipSafely();
    }

    public void DetachWindow()
    {
        Dispatcher.VerifyAccess();
        _requestedHandle = IntPtr.Zero;
        _requestedVisibleClip = null;
        DetachWindowCore();
    }

    private void DetachWindowCore()
    {
        IntPtr windowHandle = _scrcpyHandle;
        if (windowHandle != IntPtr.Zero && IsWindow(windowHandle))
        {
            _ = ShowWindow(windowHandle, SwHide);
            RestoreOriginalWindowState(windowHandle);
        }

        ResetVisibleClipSafely();
        ResetAttachedWindowState();
    }

    private void RestoreOriginalWindowState(IntPtr windowHandle)
    {
        try
        {
            Marshal.SetLastPInvokeError(0);
            IntPtr previousParent = SetParent(windowHandle, _originalParent);
            int parentError = Marshal.GetLastPInvokeError();
            if (previousParent == IntPtr.Zero && parentError != 0)
            {
                LogHostFailure(
                    $"Failed to detach the official scrcpy window from the WPF host. Error={parentError}.");
            }

            if (_originalStyleCaptured)
            {
                try
                {
                    SetWindowLongPtrChecked(windowHandle, GwlStyle, _originalStyle);
                }
                catch (Exception exception)
                {
                    LogHostFailure(
                        "Failed to restore the official scrcpy window style after detaching.",
                        exception);
                }
            }
        }
        catch (Exception exception)
        {
            LogHostFailure(
                "Failed to restore the official scrcpy window state after detaching.",
                exception);
        }
    }

    private void ResetAttachedWindowState()
    {
        _scrcpyHandle = IntPtr.Zero;
        _originalParent = IntPtr.Zero;
        _originalStyle = IntPtr.Zero;
        _originalStyleCaptured = false;
        _lastChildWidth = -1;
        _lastChildHeight = -1;
    }

    public void FocusNativeWindow()
    {
        Dispatcher.VerifyAccess();
        if (!IsInteractive)
            return;

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

        ApplyVisibleClipSafely();
        if (_requestedHandle != IntPtr.Zero)
        {
            IntPtr requestedHandle = _requestedHandle;
            try
            {
                _ = Dispatcher.BeginInvoke(
                    new Action(() => AttachRequestedWindowSafely(requestedHandle)));
            }
            catch (Exception exception)
            {
                LogHostFailure("Could not schedule the deferred scrcpy native-window attach.", exception);
                ReportAttachFailure(exception, requestedHandle);
            }
        }

        return new HandleRef(this, _containerHandle);
    }

    private void AttachRequestedWindowSafely(IntPtr requestedHandle)
    {
        if (requestedHandle == IntPtr.Zero ||
            requestedHandle != _requestedHandle ||
            _containerHandle == IntPtr.Zero)
            return;

        try
        {
            AttachWindow(requestedHandle);
        }
        catch (Exception exception)
        {
            LogHostFailure("Deferred scrcpy native-window attach failed.", exception);
            ReportAttachFailure(exception, requestedHandle);
        }
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _requestedHandle = IntPtr.Zero;
        _requestedVisibleClip = null;
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
        else if (IsInteractive &&
                 (uint)message == WmParentNotify &&
                 IsPointerDownMessage(LowWord(wParam)))
            QueueNativeFocus();
        return IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeChild();
        if (_requestedVisibleClip is not null)
            ApplyVisibleClipSafely();
    }

    protected override bool TabIntoCore(TraversalRequest request)
    {
        if (!IsInteractive)
            return false;

        FocusNativeWindow();
        return _scrcpyHandle != IntPtr.Zero;
    }

    private void ApplyVisibleClipSafely()
    {
        try
        {
            ApplyVisibleClip();
        }
        catch (Exception exception)
        {
            LogHostFailure("Failed to apply the native scrcpy visible clip.", exception);
        }
    }

    private void ApplyVisibleClip()
    {
        if (_containerHandle == IntPtr.Zero || !IsWindow(_containerHandle))
            return;

        Rect? requestedClip = _requestedVisibleClip;
        if (requestedClip is null)
        {
            SetNativeWindowRegion(_containerHandle, IntPtr.Zero);
            return;
        }

        (double scaleX, double scaleY) = DpiHelper.GetDpiScale(this);
        Rect pixelClip = ViewMultipleDevicesLayout.ConvertDipRectToDevicePixels(
            requestedClip.Value,
            scaleX,
            scaleY);

        int left = pixelClip.IsEmpty ? 0 : ToNativeCoordinate(pixelClip.Left);
        int top = pixelClip.IsEmpty ? 0 : ToNativeCoordinate(pixelClip.Top);
        int right = pixelClip.IsEmpty ? 0 : ToNativeCoordinate(pixelClip.Right);
        int bottom = pixelClip.IsEmpty ? 0 : ToNativeCoordinate(pixelClip.Bottom);
        IntPtr region = CreateRectRgn(left, top, right, bottom);
        if (region == IntPtr.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(
                error == 0 ? 1 : error,
                "Failed to create the native scrcpy clipping region.");
        }

        // SetWindowRgn transfers ownership of a successful region handle to Windows.
        bool windowOwnsRegion = false;
        try
        {
            SetNativeWindowRegion(_containerHandle, region);
            windowOwnsRegion = true;
        }
        finally
        {
            if (!windowOwnsRegion && !DeleteObject(region))
            {
                int error = Marshal.GetLastPInvokeError();
                LogHostFailure(
                    $"Failed to release the native scrcpy clipping region. Error={error}.");
            }
        }
    }

    private void ResetVisibleClipSafely()
    {
        if (_containerHandle == IntPtr.Zero || !IsWindow(_containerHandle))
            return;

        try
        {
            SetNativeWindowRegion(_containerHandle, IntPtr.Zero);
        }
        catch (Exception exception)
        {
            LogHostFailure("Failed to reset the native scrcpy visible clip.", exception);
        }
    }

    private static int ToNativeCoordinate(double value)
    {
        if (value <= int.MinValue)
            return int.MinValue;
        if (value >= int.MaxValue)
            return int.MaxValue;
        return (int)value;
    }

    private static void SetNativeWindowRegion(IntPtr windowHandle, IntPtr region)
    {
        Marshal.SetLastPInvokeError(0);
        int result = SetWindowRgn(windowHandle, region, true);
        if (result != 0)
            return;

        int error = Marshal.GetLastPInvokeError();
        throw new Win32Exception(
            error == 0 ? 1 : error,
            "Failed to update the native scrcpy clipping region.");
    }

    private bool ResizeChild(bool frameChanged = false)
    {
        if (_containerHandle == IntPtr.Zero ||
            _scrcpyHandle == IntPtr.Zero ||
            !IsWindow(_containerHandle) ||
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

    private bool IsVerifiedEmbeddedWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            _containerHandle == IntPtr.Zero ||
            !IsWindow(_containerHandle) ||
            !IsWindow(windowHandle) ||
            GetParent(windowHandle) != _containerHandle)
        {
            return false;
        }

        long style = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
        return (style & WsChild) != 0;
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

    private void QueueNativeFocus()
    {
        try
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(FocusNativeWindowSafely));
        }
        catch (Exception exception)
        {
            LogHostFailure("Could not schedule native scrcpy focus.", exception);
        }
    }

    private void FocusNativeWindowSafely()
    {
        try
        {
            FocusNativeWindow();
        }
        catch (Exception exception)
        {
            LogHostFailure("Native scrcpy focus callback failed.", exception);
        }
    }

    private void ReportAttachFailure(Exception exception, IntPtr windowHandle)
    {
        Action<Exception, IntPtr>? handlers = AttachFailed;
        if (handlers is null)
            return;

        foreach (Action<Exception, IntPtr> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(exception, windowHandle);
            }
            catch (Exception callbackException)
            {
                LogHostFailure("A scrcpy native-window attach-failure callback failed.", callbackException);
            }
        }
    }

    private void ReportAttachSucceeded(IntPtr windowHandle)
    {
        Action<IntPtr>? handlers = AttachSucceeded;
        if (handlers is null)
            return;

        foreach (Action<IntPtr> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(windowHandle);
            }
            catch (Exception callbackException)
            {
                LogHostFailure("A scrcpy native-window attach-success callback failed.", callbackException);
            }
        }
    }

    private static void LogHostFailure(string message, Exception? exception = null)
    {
        string details = exception is null
            ? message
            : $"{message} {exception}";
        Debug.WriteLine(details);
        Trace.WriteLine(details);
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
    private static extern int SetWindowRgn(
        IntPtr windowHandle,
        IntPtr region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRectRgn(
        int left,
        int top,
        int right,
        int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);

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
