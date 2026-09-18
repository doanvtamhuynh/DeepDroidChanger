using Serilog;
using System;
using System.Runtime.ExceptionServices;
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
        private readonly ScrcpyDisplayKeyState keyState = new();
        private Position? lastPointerPosition;
        private Position? lastVirtualPointerPosition;
        private Scrcpy? subscribedScrcpy;
        private readonly ScrcpyDisplayFrameMailbox frameMailbox = new();
        private int renderGeneration;
        private int wheelRemainder;
        private int renderedFrameWidth;
        private int renderedFrameHeight;

        internal const ulong PrimaryPointerId = ulong.MaxValue;
        internal const ulong VirtualPointerId = ulong.MaxValue - 1;

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
            if (bmp is not null)
                renderTarget.Source = bmp;

            Scrcpy? current = Volatile.Read(ref subscribedScrcpy);
            if (current is not null && ReferenceEquals(current, Scrcpy))
                RenderLatestFrame(current.VideoStreamDecoder);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            // For some reason WPF doesn't focus the control on click??
            Focus();
            Scrcpy? scrcpy = Scrcpy;
            if (scrcpy is not null)
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    e.Handled = true;
                    SendKeyPress(scrcpy, AndroidKeycode.AKEYCODE_HOME);
                }
                else if (e.ChangedButton == MouseButton.XButton1)
                {
                    e.Handled = true;
                    SendKeyPress(scrcpy, AndroidKeycode.AKEYCODE_APP_SWITCH);
                }
                else if (e.ChangedButton == MouseButton.XButton2)
                {
                    e.Handled = true;
                    HandleXButton2Down(scrcpy, e.ClickCount);
                }
                else if (e.ChangedButton == MouseButton.Right)
                {
                    e.Handled = true;
                    if (!scrcpy.TrySendControlCommands(
                    [
                        new BackOrScreenOnControlMessage
                        {
                            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN
                        },
                        new BackOrScreenOnControlMessage
                        {
                            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP
                        }
                    ]))
                    {
                        log.Warning("Scrcpy rejected the mouse back-or-screen-on release batch.");
                    }
                }
                else if (e.ChangedButton == MouseButton.Left && !pointerState.IsPointerDown)
                {
                    Position? position = GetScrcpyMousePosition(e);
                    if (position is not null)
                    {
                        bool pinchRequested = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                        bool captureSucceeded = CaptureMouse();
                        if (pointerState.TryBeginPointerDown(captureSucceeded, pinchRequested))
                        {
                            e.Handled = true;
                            lastPointerPosition = position;
                            List<TouchEventControlMessage> messages =
                            [
                                CreateTouchCommand(
                                    AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN,
                                    position,
                                    PrimaryPointerId)
                            ];
                            if (pointerState.IsPinchActive)
                            {
                                lastVirtualPointerPosition = GetVirtualFingerPosition(position);
                                messages.Add(
                                    CreateTouchCommand(
                                        AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN,
                                        lastVirtualPointerPosition,
                                        VirtualPointerId));
                            }

                            if (!SendTouchBatch(scrcpy, messages))
                            {
                                pointerState.ReleaseAfterCaptureLoss();
                                lastPointerPosition = null;
                                lastVirtualPointerPosition = null;
                                if (IsMouseCaptured)
                                    ReleaseMouseCapture();
                            }
                        }
                        else if (captureSucceeded && IsMouseCaptured)
                        {
                            ReleaseMouseCapture();
                        }
                    }
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && pointerState.IsPointerDown)
            {
                e.Handled = true;
                Scrcpy? scrcpy = Scrcpy;
                Position? primaryPosition = GetScrcpyMousePosition(e) ?? lastPointerPosition;
                bool pinchActive = pointerState.IsPinchActive;
                pointerState.EndPointerUp();
                if (primaryPosition is null)
                {
                    log.Warning("Unable to recover the active pointer because no valid position was retained.");
                }
                else
                {
                    List<TouchEventControlMessage> messages =
                    [
                        CreateTouchCommand(
                            AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                            primaryPosition,
                            PrimaryPointerId)
                    ];
                    if (pinchActive)
                    {
                        Position virtualPosition = GetVirtualFingerPosition(primaryPosition);
                        lastVirtualPointerPosition = virtualPosition;
                        messages.Add(
                            CreateTouchCommand(
                                AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                                virtualPosition,
                                VirtualPointerId));
                    }

                    if (scrcpy is not null)
                        SendTouchBatch(scrcpy, messages);
                }
                lastPointerPosition = null;
                lastVirtualPointerPosition = null;
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
                Scrcpy? scrcpy = Scrcpy;
                if (position is not null && scrcpy is not null)
                {
                    lastPointerPosition = position;
                    List<TouchEventControlMessage> messages =
                    [
                        CreateTouchCommand(
                            AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                            position,
                            PrimaryPointerId)
                    ];
                    if (pointerState.IsPinchActive)
                    {
                        lastVirtualPointerPosition = GetVirtualFingerPosition(position);
                        messages.Add(
                            CreateTouchCommand(
                                AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                                lastVirtualPointerPosition,
                                VirtualPointerId));
                    }

                    SendTouchBatch(scrcpy, messages);
                }
            }

            base.OnMouseMove(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            Scrcpy? scrcpy = Scrcpy;
            Key physicalKey = NormalizePhysicalKey(e.Key, e.SystemKey);
            if (scrcpy != null && KeycodeHelper.TryConvertKey(physicalKey, out AndroidKeycode keyCode))
            {
                e.Handled = true;

                AndroidMetastate metaState = GetModifierMetastate(
                    e.KeyboardDevice,
                    physicalKey,
                    isDown: true);
                uint repeat = keyState.GetRepeatForKeyDown(physicalKey, e.IsRepeat);
                KeycodeControlMessage msg = CreateKeyDownMessage(keyCode, metaState, repeat);
                if (scrcpy.TrySendControlCommands([msg]))
                {
                    keyState.TrackKeyDown(physicalKey, keyCode, metaState, repeat);
                }
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            Scrcpy? scrcpy = Scrcpy;
            Key physicalKey = NormalizePhysicalKey(e.Key, e.SystemKey);
            if (KeycodeHelper.TryConvertKey(physicalKey, out _))
            {
                e.Handled = true;
                AndroidMetastate currentMetaState = GetModifierMetastate(
                    e.KeyboardDevice,
                    physicalKey,
                    isDown: false);
                if (keyState.TryRemove(physicalKey, out ActiveAndroidKey activeKey) &&
                    scrcpy is not null)
                {
                    if (!scrcpy.TrySendControlCommands(
                    [
                        CreateKeyReleaseMessage(activeKey, currentMetaState)
                    ]))
                    {
                        log.Warning(
                            "Scrcpy rejected the key release for {PhysicalKey}; local state was already cleared.",
                            physicalKey);
                    }
                }
            }

            base.OnKeyUp(e);
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            ReleaseActiveKeys(Scrcpy);
            base.OnLostKeyboardFocus(e);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Position? position = GetScrcpyMousePosition(e);
            Scrcpy? scrcpy = Scrcpy;
            if (scrcpy != null && position != null && e.Delta != 0)
            {
                e.Handled = true;

                long accumulated = (long)wheelRemainder + e.Delta;
                int verticalScroll = (int)(accumulated / 120);
                wheelRemainder = (int)(accumulated - (long)verticalScroll * 120);

                if (verticalScroll != 0)
                {
                    ScrollEventControlMessage msg = new()
                    {
                        Position = position,
                        VerticalScroll = verticalScroll,
                        HorizontalScroll = 0
                    };
                    scrcpy.SendControlCommand(msg);
                }
            }

            base.OnMouseWheel(e);
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            RecoverPointerWithUp(Scrcpy);

            base.OnLostMouseCapture(e);
        }


        private static TouchEventControlMessage CreateTouchCommand(
            AndroidMotionEventAction action,
            Position position,
            ulong pointerId)
        {
            return new TouchEventControlMessage
            {
                Action = action,
                PointerId = pointerId,
                Position = position,
                Buttons = ScrcpyDisplayTouchPolicy.GetButtons(action, pointerId),
                Pressure = ScrcpyDisplayTouchPolicy.GetPressure(action)
            };
        }

        private static bool SendTouchBatch(
            Scrcpy scrcpy,
            IReadOnlyList<TouchEventControlMessage> messages)
        {
            bool accepted = scrcpy.TrySendControlCommands(messages);
            foreach (TouchEventControlMessage message in messages)
            {
                log.Debug(
                    "Sending {Action} for position {PositionX}, {PositionY}",
                    message.Action,
                    message.Position.Point.X,
                    message.Position.Point.Y);
            }

            return accepted;
        }

        private static Position GetVirtualFingerPosition(Position primaryPosition)
        {
            Point virtualPoint = ScrcpyDisplayGeometry.GetVirtualFingerPosition(
                primaryPosition.Point,
                primaryPosition.ScreenSize.Width,
                primaryPosition.ScreenSize.Height);
            return new Position
            {
                Point = virtualPoint,
                ScreenSize = new ScreenSize
                {
                    Width = primaryPosition.ScreenSize.Width,
                    Height = primaryPosition.ScreenSize.Height
                }
            };
        }

        /// <summary>
        /// Releases all locally tracked pointer and keyboard input without
        /// waiting for the control channel. This is safe to call repeatedly.
        /// </summary>
        public void ReleaseActiveInput()
        {
            Scrcpy? current;
            try
            {
                current = Scrcpy;
            }
            catch (Exception exception)
            {
                log.Debug(exception, "Unable to read the Scrcpy client during input recovery.");
                current = null;
            }

            try
            {
                RecoverPointerWithUp(current);
            }
            catch (Exception exception)
            {
                log.Debug(exception, "Ignoring a failed pointer recovery during input release.");
            }

            try
            {
                ReleaseActiveKeys(current);
            }
            catch (Exception exception)
            {
                log.Debug(exception, "Ignoring a failed key recovery during input release.");
            }

            try
            {
                if (IsMouseCaptured)
                    ReleaseMouseCapture();
            }
            catch (Exception exception)
            {
                log.Debug(exception, "Ignoring a failed mouse capture release during input recovery.");
            }
        }

        private void RecoverPointerWithUp(Scrcpy? scrcpy)
        {
            if (!pointerState.IsPointerDown)
                return;

            bool pinchActive = pointerState.IsPinchActive;
            Position? primaryPosition = lastPointerPosition;
            pointerState.ReleaseAfterCaptureLoss();
            try
            {
                if (primaryPosition is null)
                {
                    log.Warning("Unable to recover the active pointer because no valid position was retained.");
                }
                else if (scrcpy is not null)
                {
                    SendTouchBatch(
                        scrcpy,
                        CreatePointerRecoveryMessages(primaryPosition, pinchActive));
                }
            }
            finally
            {
                lastPointerPosition = null;
                lastVirtualPointerPosition = null;
            }
        }

        private void ReleaseActiveKeys(Scrcpy? scrcpy)
        {
            IReadOnlyList<ActiveAndroidKey> activeKeys = keyState.SnapshotAndClear();
            if (scrcpy is null || activeKeys.Count == 0)
                return;

            if (!scrcpy.TrySendControlCommands(CreateKeyReleaseMessages(activeKeys)))
            {
                log.Warning(
                    "Scrcpy rejected the focus-loss release batch for {KeyCount} active keys.",
                    activeKeys.Count);
            }
        }

        internal static KeycodeControlMessage CreateKeyDownMessage(
            AndroidKeycode keyCode,
            AndroidMetastate metaState,
            uint repeat)
        {
            return new KeycodeControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN,
                KeyCode = keyCode,
                Repeat = repeat,
                Metastate = metaState
            };
        }

        internal static IReadOnlyList<IControlMessage> CreateKeyReleaseMessages(
            IReadOnlyList<ActiveAndroidKey> activeKeys)
        {
            ArgumentNullException.ThrowIfNull(activeKeys);
            List<ActiveAndroidKey> orderedKeys = new(activeKeys);
            orderedKeys.Sort(static (left, right) =>
            {
                int priority = GetForcedReleasePriority(left.PhysicalKey)
                    .CompareTo(GetForcedReleasePriority(right.PhysicalKey));
                return priority != 0
                    ? priority
                    : ((int)left.PhysicalKey).CompareTo((int)right.PhysicalKey);
            });

            bool leftCtrl = false;
            bool rightCtrl = false;
            bool leftShift = false;
            bool rightShift = false;
            foreach (ActiveAndroidKey activeKey in orderedKeys)
            {
                switch (activeKey.PhysicalKey)
                {
                    case Key.LeftCtrl:
                        leftCtrl = true;
                        break;
                    case Key.RightCtrl:
                        rightCtrl = true;
                        break;
                    case Key.LeftShift:
                        leftShift = true;
                        break;
                    case Key.RightShift:
                        rightShift = true;
                        break;
                }
            }

            List<IControlMessage> releases = new(orderedKeys.Count);
            foreach (ActiveAndroidKey activeKey in orderedKeys)
            {
                AndroidMetastate metaState = IsModifierKey(activeKey.PhysicalKey)
                    ? KeycodeHelper.ConvertModifiers(
                        leftCtrl && activeKey.PhysicalKey != Key.LeftCtrl,
                        rightCtrl && activeKey.PhysicalKey != Key.RightCtrl,
                        leftShift && activeKey.PhysicalKey != Key.LeftShift,
                        rightShift && activeKey.PhysicalKey != Key.RightShift)
                    : activeKey.LastMetaState;
                releases.Add(CreateKeyReleaseMessage(activeKey, metaState));

                switch (activeKey.PhysicalKey)
                {
                    case Key.LeftCtrl:
                        leftCtrl = false;
                        break;
                    case Key.RightCtrl:
                        rightCtrl = false;
                        break;
                    case Key.LeftShift:
                        leftShift = false;
                        break;
                    case Key.RightShift:
                        rightShift = false;
                        break;
                }
            }

            return releases;
        }

        private static bool IsModifierKey(Key key)
        {
            return key is Key.LeftCtrl or
                Key.RightCtrl or
                Key.LeftShift or
                Key.RightShift;
        }

        private static int GetForcedReleasePriority(Key key)
        {
            return key switch
            {
                Key.LeftCtrl or Key.RightCtrl => 2,
                Key.LeftShift or Key.RightShift => 1,
                _ => 0
            };
        }

        internal static KeycodeControlMessage CreateKeyReleaseMessage(
            ActiveAndroidKey activeKey,
            AndroidMetastate metaState)
        {
            return new KeycodeControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
                KeyCode = activeKey.AndroidKeyCode,
                Metastate = metaState
            };
        }

        internal static bool ShouldRecoverPointerForFrameSizeChange(
            int renderedWidth,
            int renderedHeight,
            int nextWidth,
            int nextHeight,
            bool pointerIsActive)
        {
            return pointerIsActive &&
                   (renderedWidth != nextWidth || renderedHeight != nextHeight);
        }
        internal static IReadOnlyList<TouchEventControlMessage> CreatePointerRecoveryMessages(
            Position primaryPosition,
            bool pinchActive)
        {
            ArgumentNullException.ThrowIfNull(primaryPosition);
            List<TouchEventControlMessage> messages =
            [
                CreateTouchCommand(
                    AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                    primaryPosition,
                    PrimaryPointerId)
            ];
            if (pinchActive)
            {
                messages.Add(
                    CreateTouchCommand(
                        AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                        GetVirtualFingerPosition(primaryPosition),
                        VirtualPointerId));
            }
            return messages;
        }

        private Position? GetScrcpyMousePosition(MouseEventArgs e)
        {
            Scrcpy? scrcpy = Scrcpy;
            if (scrcpy == null ||
                renderTarget == null ||
                renderedFrameWidth <= 0 ||
                renderedFrameHeight <= 0)
            {
                return null;
            }

            WpfPoint point = e.GetPosition(renderTarget);
            if (!TryMapPointToRenderedFrame(
                    point,
                    renderedFrameWidth,
                    renderedFrameHeight,
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
                    Width = checked((ushort)renderedFrameWidth),
                    Height = checked((ushort)renderedFrameHeight)
                }
            };
        }

        private void OnFrame(object? sender, FrameData frameData)
        {
            if (sender is not VideoStreamDecoder sourceDecoder ||
                !IsCurrentFrameSource(sourceDecoder) ||
                renderTarget == null)
            {
                return;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            try
            {
                bool shouldSchedule = frameMailbox.TryPublish(
                    sourceDecoder,
                    Volatile.Read(ref renderGeneration),
                    frameData.Width,
                    frameData.Height,
                    frameData.Data);
                if (shouldSchedule)
                    ScheduleFrameRender();
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

        private void ScheduleFrameRender()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                frameMailbox.AbortScheduledRender();
                return;
            }

            try
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(RenderPendingFrame));
            }
            catch (ObjectDisposedException)
            {
                frameMailbox.AbortScheduledRender();
            }
            catch (InvalidOperationException)
            {
                frameMailbox.AbortScheduledRender();
            }
        }

        private void RenderPendingFrame()
        {
            bool scheduleAgain = ProcessPendingFrame(
                frameMailbox,
                frame =>
                {
                    if (frame.SourceDecoder is not null &&
                        frame.Generation == Volatile.Read(ref renderGeneration) &&
                        IsCurrentFrameSource(frame.SourceDecoder) &&
                        frame.Buffer is not null)
                    {
                        RenderFrame(
                            frame.SourceDecoder,
                            frame.Width,
                            frame.Height,
                            frame.Buffer.AsSpan(0, frame.Length));
                    }
                });

            if (scheduleAgain)
                ScheduleFrameRender();
        }

        internal static bool ProcessPendingFrame(
            ScrcpyDisplayFrameMailbox frameMailbox,
            Action<ScrcpyDisplayFrameMailbox.PendingFrame> render)
        {
            ArgumentNullException.ThrowIfNull(frameMailbox);
            ArgumentNullException.ThrowIfNull(render);

            ScrcpyDisplayFrameMailbox.PendingFrame frame = default;
            bool frameTaken = false;
            bool fatal = false;
            bool scheduleAgain = false;
            ExceptionDispatchInfo? fatalException = null;
            try
            {
                frameTaken = frameMailbox.TryTake(out frame);
                if (frameTaken)
                {
                    try
                    {
                        render(frame);
                    }
                    catch (OutOfMemoryException exception)
                    {
                        fatal = true;
                        fatalException = ExceptionDispatchInfo.Capture(exception);
                    }
                    catch (Exception exception)
                    {
                        log.Warning(exception, "Ignoring a failed decoded frame render.");
                    }
                }
            }
            catch (OutOfMemoryException exception)
            {
                fatal = true;
                fatalException = ExceptionDispatchInfo.Capture(exception);
            }
            catch (Exception exception)
            {
                log.Warning(exception, "Ignoring a failed decoded frame render.");
            }
            finally
            {
                if (frameTaken)
                    frameMailbox.Return(ref frame);

                if (fatal)
                    frameMailbox.AbortScheduledRender();
                else
                    scheduleAgain = frameMailbox.CompleteRenderAndCheckPending();
            }

            fatalException?.Throw();
            return scheduleAgain;
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
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                log.Warning(exception, "Ignoring a failed latest-frame render.");
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

            if (!TryGetFrameLayout(width, height, bgra32.Length, out int rowLength, out _))
            {
                log.Debug("Ignoring malformed or overflowing BGRA32 frame {Width}x{Height}.", width, height);
                return;
            }

            if (ShouldRecoverPointerForFrameSizeChange(
                    renderedFrameWidth,
                    renderedFrameHeight,
                    width,
                    height,
                    pointerState.IsPointerDown))
            {
                RecoverPointerWithUp(Scrcpy);
                if (IsMouseCaptured)
                    ReleaseMouseCapture();
            }

            WriteableBitmap? currentBitmap = bmp;
            bool requiresNewBitmap = currentBitmap is null ||
                currentBitmap.Width != width ||
                currentBitmap.Height != height;
            WriteableBitmap bitmap = requiresNewBitmap
                ? new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null)
                : currentBitmap!;
            if (bitmap.BackBufferStride < rowLength)
            {
                log.Debug("Ignoring frame because the WPF back-buffer stride is too small.");
                return;
            }

            bool locked = false;
            bool frameWritten = false;
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
                frameWritten = true;
            }
            finally
            {
                if (locked)
                    bitmap.Unlock();
            }

            if (frameWritten && IsCurrentFrameSource(expectedDecoder))
            {
                // A newly sized bitmap is published only after its first
                // complete frame has been copied successfully.
                if (!ReferenceEquals(renderTarget.Source, bitmap))
                    renderTarget.Source = bitmap;
                if (requiresNewBitmap)
                    bmp = bitmap;
                renderedFrameWidth = width;
                renderedFrameHeight = height;
            }
        }

        private static void OnScrcpyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not ScrcpyDisplay display)
                return;

            display.wheelRemainder = 0;
            int generation = unchecked(display.renderGeneration + 1);
            Volatile.Write(ref display.renderGeneration, generation);
            display.renderedFrameWidth = 0;
            display.renderedFrameHeight = 0;
            display.frameMailbox.SetCurrentSource(null, generation);

            if (e.OldValue is Scrcpy old)
            {
                // Release input on the old client before detaching it. The new
                // client must never inherit the old display state.
                try
                {
                    display.RecoverPointerWithUp(old);
                }
                catch (Exception exception)
                {
                    log.Debug(exception, "Ignoring a failed pointer recovery while replacing Scrcpy.");
                }

                try
                {
                    display.ReleaseActiveKeys(old);
                }
                catch (Exception exception)
                {
                    log.Debug(exception, "Ignoring a failed key recovery while replacing Scrcpy.");
                }

                try
                {
                    old.VideoStreamDecoder.OnFrame -= display.OnFrame;
                }
                catch (Exception exception)
                {
                    log.Debug(exception, "Ignoring a failed frame callback removal while replacing Scrcpy.");
                }

                try
                {
                    if (display.IsMouseCaptured)
                        display.ReleaseMouseCapture();
                }
                catch (Exception exception)
                {
                    log.Debug(exception, "Ignoring a failed mouse capture release while replacing Scrcpy.");
                }
            }

            display.keyState.Clear();

            Scrcpy? current = e.NewValue as Scrcpy;
            if (current is not null)
            {
                // Publish and subscribe before taking the snapshot so a frame
                // arriving during binding has no subscription gap.
                Volatile.Write(ref display.subscribedScrcpy, current);
                current.VideoStreamDecoder.OnFrame += display.OnFrame;
                display.frameMailbox.SetCurrentSource(current.VideoStreamDecoder, generation);
            }

            display.bmp = null;
            if (display.renderTarget != null)
                display.renderTarget.Source = null;

            if (current is not null)
                display.RenderLatestFrame(current.VideoStreamDecoder);
            else
                Volatile.Write(ref display.subscribedScrcpy, null);
        }

        internal static Key NormalizePhysicalKey(Key key, Key systemKey)
        {
            return key == Key.System ? systemKey : key;
        }

        private static AndroidMetastate GetModifierMetastate(
            KeyboardDevice keyboardDevice,
            Key physicalKey,
            bool isDown)
        {
            bool leftCtrl = keyboardDevice.IsKeyDown(Key.LeftCtrl);
            bool rightCtrl = keyboardDevice.IsKeyDown(Key.RightCtrl);
            bool leftShift = keyboardDevice.IsKeyDown(Key.LeftShift);
            bool rightShift = keyboardDevice.IsKeyDown(Key.RightShift);

            // WPF can report the modifier transition before the device state
            // is updated for the event. Correct only the event's own key so
            // the snapshot still reflects all other physical modifiers.
            if (physicalKey == Key.LeftCtrl)
                leftCtrl = isDown;
            else if (physicalKey == Key.RightCtrl)
                rightCtrl = isDown;
            else if (physicalKey == Key.LeftShift)
                leftShift = isDown;
            else if (physicalKey == Key.RightShift)
                rightShift = isDown;

            return KeycodeHelper.ConvertModifiers(
                leftCtrl,
                rightCtrl,
                leftShift,
                rightShift);
        }

        internal static bool IsCurrentFrameSource(
            VideoStreamDecoder? currentDecoder,
            object? sender)
        {
            return currentDecoder is not null &&
                   sender is VideoStreamDecoder decoder &&
                   ReferenceEquals(decoder, currentDecoder);
        }

        internal static bool TryMapPointToRenderedFrame(
            WpfPoint point,
            int renderedWidth,
            int renderedHeight,
            double targetWidth,
            double targetHeight,
            out WpfPoint devicePoint)
        {
            if (renderedWidth <= 0 || renderedHeight <= 0)
            {
                devicePoint = default;
                return false;
            }

            return ScrcpyDisplayGeometry.TryMapPointToDevice(
                point,
                renderedWidth,
                renderedHeight,
                targetWidth,
                targetHeight,
                out devicePoint);
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

        private void HandleXButton2Down(Scrcpy scrcpy, int clickCount)
        {
            IControlMessage message = clickCount >= 2
                ? new ExpandSettingsPanelControlMessage()
                : new ExpandNotificationPanelControlMessage();
            if (!scrcpy.TrySendControlCommands([message]))
                log.Warning("Scrcpy rejected the XButton2 panel action.");
        }

        private static void SendKeyPress(Scrcpy scrcpy, AndroidKeycode keyCode)
        {
            if (!scrcpy.TrySendControlCommands(
            [
                new KeycodeControlMessage
                {
                    Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN,
                    KeyCode = keyCode
                },
                new KeycodeControlMessage
                {
                    Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
                    KeyCode = keyCode
                }
            ]))
            {
                log.Warning("Scrcpy rejected the {KeyCode} key press batch.", keyCode);
            }
        }
    }
}
