using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfPoint = System.Windows.Point;

namespace ScrcpyNet.Wpf
{
    /// <summary>
    /// Follow steps 1a or 1b and then 2 to use this custom control in a XAML file.
    ///
    /// Step 1a) Using this custom control in a XAML file that exists in the current project.
    /// Add this XmlNamespace attribute to the root element of the markup file where it is
    /// to be used:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ScrcpyNet.Wpf"
    ///
    /// Step 1b) Using this custom control in a XAML file that exists in a different project.
    /// Add this XmlNamespace attribute to the root element of the markup file where it is
    /// to be used:
    ///
    ///     xmlns:MyNamespace="clr-namespace:ScrcpyNet.Wpf;assembly=DeepDroidChanger.ViewDevices"
    ///
    /// You will also need to add a project reference from the project where the XAML file lives
    /// to this project and Rebuild to avoid compilation errors.
    ///
    /// Step 2) Go ahead and use the control in a XAML file:
    ///
    ///     <MyNamespace:ScrcpyDisplay/>
    ///
    /// </summary>
    public class ScrcpyDisplay : Control
    {
        private static readonly ILogger log = Log.ForContext<ScrcpyDisplay>();

        public static readonly DependencyProperty ScrcpyProperty = DependencyProperty.Register(
            nameof(Scrcpy),
            typeof(Scrcpy),
            typeof(ScrcpyDisplay),
            new PropertyMetadata(OnScrcpyChanged));

        private Image? renderTarget;
        private WriteableBitmap? bmp;
        private readonly ScrcpyDisplayPointerState pointerState = new();
        private Position? lastPointerPosition;
        private Scrcpy? subscribedScrcpy;

        static ScrcpyDisplay()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ScrcpyDisplay),
                new FrameworkPropertyMetadata(typeof(ScrcpyDisplay)));
        }

        public Scrcpy? Scrcpy
        {
            get => (Scrcpy?)GetValue(ScrcpyProperty);
            set => SetValue(ScrcpyProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            renderTarget = GetTemplateChild("PART_RenderTargetImage") as Image;
            if (renderTarget is null)
                return;

            renderTarget.Stretch = Stretch.Uniform;

            Scrcpy? current = Volatile.Read(ref subscribedScrcpy);
            if (current is not null && ReferenceEquals(current, Scrcpy))
                RenderLatestFrame(current.VideoStreamDecoder);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            // For some reason WPF doesn't focus the control on click??
            Focus();

            if (Scrcpy != null)
            {
                if (e.RightButton == MouseButtonState.Pressed)
                {
                    e.Handled = true;
                    Scrcpy.SendControlCommand(new BackOrScreenOnControlMessage
                    {
                        Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN
                    });
                    Scrcpy.SendControlCommand(new BackOrScreenOnControlMessage
                    {
                        Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP
                    });
                }
                else if (e.LeftButton == MouseButtonState.Pressed)
                {
                    Position? position = GetScrcpyMousePosition(e);
                    if (position != null && pointerState.TryBeginPointerDown(CaptureMouse()))
                    {
                        e.Handled = true;
                        lastPointerPosition = position;
                        SendTouchCommand(
                            AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN,
                            position,
                            Scrcpy);
                    }
                }
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && pointerState.EndPointerUp())
            {
                e.Handled = true;
                SendTouchCommand(
                    AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                    GetScrcpyMousePosition(e) ?? lastPointerPosition,
                    Scrcpy);
                lastPointerPosition = null;
                if (IsMouseCaptured)
                    ReleaseMouseCapture();
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (pointerState.CanMove)
            {
                Position? position = GetScrcpyMousePosition(e);
                if (position != null)
                    lastPointerPosition = position;
                SendTouchCommand(
                    AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                    position,
                    Scrcpy);
            }

            base.OnMouseMove(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            Scrcpy? scrcpy = Scrcpy;
            if (scrcpy != null && KeycodeHelper.TryConvertKey(e.Key, out AndroidKeycode keyCode))
            {
                e.Handled = true;

                KeycodeControlMessage msg = new()
                {
                    KeyCode = keyCode,
                    Metastate = KeycodeHelper.ConvertModifiers(e.KeyboardDevice.Modifiers)
                };
                scrcpy.SendControlCommand(msg);
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            Scrcpy? scrcpy = Scrcpy;
            if (scrcpy != null && KeycodeHelper.TryConvertKey(e.Key, out AndroidKeycode keyCode))
            {
                e.Handled = true;

                KeycodeControlMessage msg = new()
                {
                    Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
                    KeyCode = keyCode,
                    Metastate = KeycodeHelper.ConvertModifiers(e.KeyboardDevice.Modifiers)
                };
                scrcpy.SendControlCommand(msg);
            }

            base.OnKeyUp(e);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Position? pos = GetScrcpyMousePosition(e);
            if (Scrcpy != null && pos != null)
            {
                e.Handled = true;

                ScrollEventControlMessage msg = new()
                {
                    Position = pos,
                    VerticalScroll = e.Delta / 120,
                    HorizontalScroll = 0
                };
                Scrcpy.SendControlCommand(msg);
            }

            base.OnMouseWheel(e);
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            if (pointerState.CancelPointer())
            {
                Position? position = lastPointerPosition;
                lastPointerPosition = null;
                SendTouchCommand(
                    AndroidMotionEventAction.AMOTION_EVENT_ACTION_CANCEL,
                    position,
                    Scrcpy);
            }

            base.OnLostMouseCapture(e);
        }

        private void SendTouchCommand(
            AndroidMotionEventAction action,
            Position? position,
            Scrcpy? scrcpy)
        {
            if (scrcpy == null || position == null)
                return;

            TouchEventControlMessage msg = new()
            {
                Action = action,
                Position = position,
                Buttons = action is AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP or
                    AndroidMotionEventAction.AMOTION_EVENT_ACTION_CANCEL
                    ? (AndroidMotionEventButtons)0
                    : AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY,
                Pressure = action is AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP or
                    AndroidMotionEventAction.AMOTION_EVENT_ACTION_CANCEL
                    ? 0f
                    : 1f
            };
            scrcpy.SendControlCommand(msg);

            log.Debug(
                "Sending {Action} for position {PositionX}, {PositionY}",
                action,
                msg.Position.Point.X,
                msg.Position.Point.Y);
        }

        private Position? GetScrcpyMousePosition(MouseEventArgs e)
        {
            Scrcpy? scrcpy = Scrcpy;
            if (scrcpy == null || renderTarget == null)
                return null;

            WpfPoint point = e.GetPosition(renderTarget);
            if (!ScrcpyDisplayGeometry.TryMapPointToDevice(
                    point,
                    scrcpy.Width,
                    scrcpy.Height,
                    renderTarget.ActualWidth,
                    renderTarget.ActualHeight,
                    out WpfPoint devicePoint))
            {
                return null;
            }

            return new Position
            {
                Point = new ScrcpyNet.Point
                {
                    X = (int)devicePoint.X,
                    Y = (int)devicePoint.Y
                },
                ScreenSize = new ScreenSize
                {
                    Width = checked((ushort)scrcpy.Width),
                    Height = checked((ushort)scrcpy.Height)
                }
            };
        }

        private void OnFrame(object? sender, FrameData _)
        {
            if (sender is not VideoStreamDecoder sourceDecoder ||
                !IsCurrentFrameSource(sourceDecoder) ||
                renderTarget == null)
            {
                return;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;
            DecodedFrameSnapshot? snapshot;
            try
            {
                snapshot = sourceDecoder.CaptureLatestFrame();
            }
            catch (ObjectDisposedException)
            {
                log.Debug("Ignoring latest-frame capture from a disposed decoder.");
                return;
            }

            if (snapshot is null)
                return;

            // The source decoder is captured before dispatch so a client replacement
            // cannot make an old callback paint a frame from the new client.
            try
            {
                // The timeout is required. Otherwise this can block forever when the
                // application is about to exit but the video thread sends a last frame.
                Dispatcher.Invoke(
                    () =>
                    {
                        if (Dispatcher.HasShutdownStarted ||
                            Dispatcher.HasShutdownFinished ||
                            renderTarget == null ||
                            !IsCurrentFrameSource(sourceDecoder))
                        {
                            return;
                        }

                        RenderFrame(
                            sourceDecoder,
                            snapshot.Width,
                            snapshot.Height,
                            snapshot.Bgra32.Span);
                    },
                    DispatcherPriority.Send,
                    default,
                    TimeSpan.FromMilliseconds(200));
            }
            catch (TimeoutException)
            {
                log.Debug("Ignoring TimeoutException inside OnFrame.");
            }
            catch (TaskCanceledException)
            {
                log.Debug("Ignoring TaskCanceledException inside OnFrame.");
            }
            catch (ObjectDisposedException)
            {
                log.Debug("Ignoring ObjectDisposedException inside OnFrame during control disposal.");
            }
            catch (InvalidOperationException)
            {
                log.Debug("Ignoring InvalidOperationException inside OnFrame during dispatcher shutdown.");
            }
        }

        private void RenderLatestFrame(VideoStreamDecoder expectedDecoder)
        {
            if (!IsCurrentFrameSource(expectedDecoder) || renderTarget == null)
                return;

            try
            {
                DecodedFrameSnapshot? snapshot = expectedDecoder.CaptureLatestFrame();
                if (snapshot is not null)
                {
                    RenderFrame(
                        expectedDecoder,
                        snapshot.Width,
                        snapshot.Height,
                        snapshot.Bgra32.Span);
                }
            }
            catch (ObjectDisposedException)
            {
                log.Debug("Ignoring latest-frame capture from a disposed decoder.");
            }
        }

        private unsafe void RenderFrame(
            VideoStreamDecoder expectedDecoder,
            int width,
            int height,
            ReadOnlySpan<byte> bgra32)
        {
            if (!IsCurrentFrameSource(expectedDecoder) || renderTarget == null)
                return;
            if (width <= 0 || height <= 0)
            {
                log.Debug("Ignoring decoded frame with invalid dimensions {Width}x{Height}.", width, height);
                return;
            }

            if (!TryGetFrameLayout(width, height, bgra32.Length, out int rowLength, out int expectedLength))
            {
                log.Debug("Ignoring malformed or overflowing BGRA32 frame {Width}x{Height}.", width, height);
                return;
            }

            if (bmp == null || bmp.Width != width || bmp.Height != height)
            {
                bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                renderTarget.Source = bmp;
            }

            WriteableBitmap bitmap = bmp;
            if (bitmap.BackBufferStride < rowLength)
            {
                log.Debug("Ignoring frame because the WPF back-buffer stride is too small.");
                return;
            }

            bool locked = false;
            try
            {
                bitmap.Lock();
                locked = true;
                if (!IsCurrentFrameSource(expectedDecoder) || renderTarget == null)
                    return;

                for (int y = 0; y < height; y++)
                {
                    int offset = checked(y * rowLength);
                    bgra32.Slice(offset, rowLength).CopyTo(
                        new Span<byte>(
                            (byte*)bitmap.BackBuffer + y * bitmap.BackBufferStride,
                            rowLength));
                }

                bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                if (locked)
                    bitmap.Unlock();
            }
        }

        private static void OnScrcpyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not ScrcpyDisplay display)
                return;

            if (e.OldValue is Scrcpy old)
            {
                old.VideoStreamDecoder.OnFrame -= display.OnFrame;
                display.CancelPointer(old);
            }

            Scrcpy? current = e.NewValue as Scrcpy;
            if (current is not null)
            {
                // Publish and subscribe before taking the snapshot so a frame
                // arriving during binding has no subscription gap.
                Volatile.Write(ref display.subscribedScrcpy, current);
                current.VideoStreamDecoder.OnFrame += display.OnFrame;
            }

            display.bmp = null;
            if (display.renderTarget != null)
                display.renderTarget.Source = null;

            if (current is not null)
                display.RenderLatestFrame(current.VideoStreamDecoder);
            else
                Volatile.Write(ref display.subscribedScrcpy, null);
        }

        internal static bool IsCurrentFrameSource(
            VideoStreamDecoder? currentDecoder,
            object? sender)
        {
            return currentDecoder is not null &&
                   sender is VideoStreamDecoder decoder &&
                   ReferenceEquals(decoder, currentDecoder);
        }

        internal static bool TryGetFrameLayout(
            int width,
            int height,
            int bufferLength,
            out int rowLength,
            out int expectedLength)
        {
            rowLength = 0;
            expectedLength = 0;
            if (width <= 0 || height <= 0 || bufferLength < 0)
                return false;

            try
            {
                rowLength = checked(width * 4);
                expectedLength = checked(rowLength * height);
            }
            catch (OverflowException)
            {
                return false;
            }

            return expectedLength == bufferLength;
        }

        private bool IsCurrentFrameSource(VideoStreamDecoder decoder)
        {
            Scrcpy? current = Volatile.Read(ref subscribedScrcpy);
            return current is not null &&
                   IsCurrentFrameSource(current.VideoStreamDecoder, decoder);
        }

        private void CancelPointer(Scrcpy? scrcpy)
        {
            if (!pointerState.CancelPointer())
                return;

            Position? position = lastPointerPosition;
            lastPointerPosition = null;
            SendTouchCommand(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_CANCEL,
                position,
                scrcpy);
            if (IsMouseCaptured)
                ReleaseMouseCapture();
        }
    }
}
