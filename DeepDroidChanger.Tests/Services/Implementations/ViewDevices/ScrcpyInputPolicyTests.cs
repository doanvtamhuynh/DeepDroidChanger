using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using DeepDroidChanger.Views;
using ScrcpyNet;
using ScrcpyNet.Wpf;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ScrcpyInputPolicyTests
{
    [TestMethod]
    public void CtrlV_ResolvesToPaste()
    {
        ViewDeviceShortcutResolution result = Resolve(Key.V, ModifierKeys.Control);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.Paste, result.Action);
    }

    [TestMethod]
    public void CtrlAlone_IsNotConsumedByViewerShortcutResolver()
    {
        ViewDeviceShortcutResolution result = Resolve(
            Key.LeftCtrl,
            ModifierKeys.Control);

        Assert.IsFalse(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.None, result.Action);
    }

    [TestMethod]
    public void CtrlV_FirstKeyDown_PastesOnce()
    {
        ViewDeviceShortcutResolver resolver = new();

        ViewDeviceShortcutResolution result = resolver.Resolve(
            Key.V,
            ModifierKeys.Control,
            isRepeat: false);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.Paste, result.Action);
    }

    [TestMethod]
    public void CtrlV_Repeat_DoesNotPasteAgain()
    {
        ViewDeviceShortcutResolver resolver = new();
        ViewDeviceShortcutResolution first = resolver.Resolve(
            Key.V,
            ModifierKeys.Control,
            isRepeat: false);
        Assert.IsTrue(ViewDeviceShortcutPolicy.ShouldExecute(first, isRepeat: false));

        ViewDeviceShortcutResolution repeat = resolver.Resolve(
            Key.V,
            ModifierKeys.Control,
            isRepeat: true);

        Assert.IsTrue(repeat.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.Paste, repeat.Action);
        Assert.IsFalse(ViewDeviceShortcutPolicy.ShouldExecute(repeat, isRepeat: true));
    }

    [TestMethod]
    public void CtrlV_NonRepeatKeyDown_RemainsEligibleAfterRepeat()
    {
        ViewDeviceShortcutResolver resolver = new();

        _ = resolver.Resolve(Key.V, ModifierKeys.Control, isRepeat: false);
        _ = resolver.Resolve(Key.V, ModifierKeys.Control, isRepeat: true);
        ViewDeviceShortcutResolution result = resolver.Resolve(
            Key.V,
            ModifierKeys.Control,
            isRepeat: false);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.Paste, result.Action);
        Assert.IsTrue(ViewDeviceShortcutPolicy.ShouldExecute(result, isRepeat: false));
    }

    [TestMethod]
    public void CtrlDown_CanBeTrackedAsActiveKey()
    {
        ScrcpyDisplayKeyState state = new();

        state.TrackKeyDown(
            Key.LeftCtrl,
            AndroidKeycode.AKEYCODE_CTRL_LEFT,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        Assert.IsTrue(state.TryRemove(Key.LeftCtrl, out ActiveAndroidKey activeKey));
        Assert.AreEqual(AndroidKeycode.AKEYCODE_CTRL_LEFT, activeKey.AndroidKeyCode);
    }

    [TestMethod]
    public void CtrlUp_ReleasesTrackedCtrl()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.RightCtrl,
            AndroidKeycode.AKEYCODE_CTRL_RIGHT,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        Assert.IsTrue(state.TryRemove(Key.RightCtrl, out ActiveAndroidKey activeKey));
        KeycodeControlMessage release = ScrcpyDisplay.CreateKeyReleaseMessage(
            activeKey,
            AndroidMetastate.AMETA_NONE);

        Assert.AreEqual(AndroidKeycode.AKEYCODE_CTRL_RIGHT, release.KeyCode);
        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP, release.Action);
        Assert.AreEqual(0, state.Count);
    }

    [TestMethod]
    public void LostKeyboardFocus_ReleasesActiveCtrl()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.LeftCtrl,
            AndroidKeycode.AKEYCODE_CTRL_LEFT,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        IReadOnlyList<IControlMessage> releases =
            ScrcpyDisplay.CreateKeyReleaseMessages(state.SnapshotAndClear());

        Assert.HasCount(1, releases);
        KeycodeControlMessage release = (KeycodeControlMessage)releases[0];
        Assert.AreEqual(AndroidKeycode.AKEYCODE_CTRL_LEFT, release.KeyCode);
        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP, release.Action);
        Assert.AreEqual(0, state.Count);
    }

    [TestMethod]
    public void ClientReplacement_ReleasesActiveCtrl()
    {
        ScrcpyDisplayKeyState oldClientState = new();
        oldClientState.TrackKeyDown(
            Key.RightCtrl,
            AndroidKeycode.AKEYCODE_CTRL_RIGHT,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);
        ScrcpyDisplayKeyState newClientState = new();

        IReadOnlyList<IControlMessage> releases =
            ScrcpyDisplay.CreateKeyReleaseMessages(oldClientState.SnapshotAndClear());

        Assert.HasCount(1, releases);
        Assert.AreEqual(
            AndroidKeycode.AKEYCODE_CTRL_RIGHT,
            ((KeycodeControlMessage)releases[0]).KeyCode);
        Assert.AreEqual(0, newClientState.Count);
    }

    [TestMethod]
    public void CtrlV_KeyUpWithoutForwardedVDown_DoesNotProduceDuplicateRelease()
    {
        ScrcpyDisplayKeyState state = new();

        Assert.IsFalse(state.TryRemove(Key.V, out _));
    }

    [TestMethod]
    public void CtrlAndShiftMappings_UseOfficialSideSpecificAndroidKeycodes()
    {
        (Key physicalKey, AndroidKeycode androidKeycode)[] mappings =
        [
            (Key.LeftCtrl, AndroidKeycode.AKEYCODE_CTRL_LEFT),
            (Key.RightCtrl, AndroidKeycode.AKEYCODE_CTRL_RIGHT),
            (Key.LeftShift, AndroidKeycode.AKEYCODE_SHIFT_LEFT),
            (Key.RightShift, AndroidKeycode.AKEYCODE_SHIFT_RIGHT)
        ];

        foreach ((Key physicalKey, AndroidKeycode androidKeycode) in mappings)
            Assert.AreEqual(androidKeycode, KeycodeHelper.ConvertKey(physicalKey));
    }

    [TestMethod]
    public void Alt_IsStillNotForwardedAsOrdinaryAndroidInput()
    {
        Assert.IsFalse(KeycodeHelper.TryConvertKey(Key.LeftAlt, out _));
        Assert.IsFalse(KeycodeHelper.TryConvertKey(Key.RightAlt, out _));
    }

    [TestMethod]
    public void LeftCtrl_MetaContainsLeftAndAggregate()
    {
        Assert.AreEqual(
            AndroidMetastate.AMETA_CTRL_LEFT_ON | AndroidMetastate.AMETA_CTRL_ON,
            KeycodeHelper.ConvertModifiers(true, false, false, false));
    }

    [TestMethod]
    public void RightCtrl_MetaContainsRightAndAggregate()
    {
        Assert.AreEqual(
            AndroidMetastate.AMETA_CTRL_RIGHT_ON | AndroidMetastate.AMETA_CTRL_ON,
            KeycodeHelper.ConvertModifiers(false, true, false, false));
    }

    [TestMethod]
    public void BothCtrl_MetaContainsBothAndAggregate()
    {
        Assert.AreEqual(
            AndroidMetastate.AMETA_CTRL_LEFT_ON |
            AndroidMetastate.AMETA_CTRL_RIGHT_ON |
            AndroidMetastate.AMETA_CTRL_ON,
            KeycodeHelper.ConvertModifiers(true, true, false, false));
    }

    [TestMethod]
    public void LeftShift_MetaContainsLeftAndAggregate()
    {
        Assert.AreEqual(
            AndroidMetastate.AMETA_SHIFT_LEFT_ON | AndroidMetastate.AMETA_SHIFT_ON,
            KeycodeHelper.ConvertModifiers(false, false, true, false));
    }

    [TestMethod]
    public void RightShift_MetaContainsRightAndAggregate()
    {
        Assert.AreEqual(
            AndroidMetastate.AMETA_SHIFT_RIGHT_ON | AndroidMetastate.AMETA_SHIFT_ON,
            KeycodeHelper.ConvertModifiers(false, false, false, true));
    }

    [TestMethod]
    public void BothShift_MetaContainsBothAndAggregate()
    {
        Assert.AreEqual(
            AndroidMetastate.AMETA_SHIFT_LEFT_ON |
            AndroidMetastate.AMETA_SHIFT_RIGHT_ON |
            AndroidMetastate.AMETA_SHIFT_ON,
            KeycodeHelper.ConvertModifiers(false, false, true, true));
    }

    [TestMethod]
    public void NoModifiers_ReturnsNone()
    {
        Assert.AreEqual(
            AndroidMetastate.AMETA_NONE,
            KeycodeHelper.ConvertModifiers(false, false, false, false));
    }

    [TestMethod]
    public void CtrlC_UsesSideSpecificCtrlMetastate()
    {
        KeycodeControlMessage message = ScrcpyDisplay.CreateKeyDownMessage(
            AndroidKeycode.AKEYCODE_C,
            KeycodeHelper.ConvertModifiers(true, false, false, false),
            repeat: 0);

        Assert.AreEqual(
            AndroidMetastate.AMETA_CTRL_LEFT_ON | AndroidMetastate.AMETA_CTRL_ON,
            message.Metastate);
    }

    [TestMethod]
    public void CtrlA_UsesSideSpecificCtrlMetastate()
    {
        KeycodeControlMessage message = ScrcpyDisplay.CreateKeyDownMessage(
            AndroidKeycode.AKEYCODE_A,
            KeycodeHelper.ConvertModifiers(false, true, false, false),
            repeat: 0);

        Assert.AreEqual(
            AndroidMetastate.AMETA_CTRL_RIGHT_ON | AndroidMetastate.AMETA_CTRL_ON,
            message.Metastate);
    }

    [TestMethod]
    public void ShiftLetter_UsesSideSpecificShiftMetastate()
    {
        KeycodeControlMessage message = ScrcpyDisplay.CreateKeyDownMessage(
            AndroidKeycode.AKEYCODE_H,
            KeycodeHelper.ConvertModifiers(false, false, true, false),
            repeat: 0);

        Assert.AreEqual(
            AndroidMetastate.AMETA_SHIFT_LEFT_ON | AndroidMetastate.AMETA_SHIFT_ON,
            message.Metastate);
    }

    [TestMethod]
    public void CtrlShiftV_IsNotClaimed()
    {
        ViewDeviceShortcutResolution result = Resolve(
            Key.V,
            ModifierKeys.Control | ModifierKeys.Shift);

        Assert.IsFalse(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.None, result.Action);
    }

    [TestMethod]
    public void AltV_ResolvesToPasteWithPasteKey()
    {
        ViewDeviceShortcutResolution result = Resolve(Key.V, ModifierKeys.Alt);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.PasteWithPasteKey, result.Action);
    }

    [TestMethod]
    public void AltV_DoesNotProduceVKeyAction()
    {
        Assert.AreNotEqual(
            ViewDeviceShortcutAction.Paste,
            Resolve(Key.V, ModifierKeys.Alt).Action);
    }

    [TestMethod]
    public void AltShiftV_ResolvesToClipboardTextInjection()
    {
        Assert.AreEqual(
            ViewDeviceShortcutAction.InjectClipboardText,
            Resolve(Key.V, ModifierKeys.Alt | ModifierKeys.Shift).Action);
    }

    [TestMethod]
    public void AltC_ResolvesToCopyAndAltXToCut()
    {
        Assert.AreEqual(ViewDeviceShortcutAction.Copy, Resolve(Key.C, ModifierKeys.Alt).Action);
        Assert.AreEqual(ViewDeviceShortcutAction.Cut, Resolve(Key.X, ModifierKeys.Alt).Action);
    }

    [TestMethod]
    public void NormalCtrlCopyAndCutAreForwarded()
    {
        Assert.IsFalse(Resolve(Key.C, ModifierKeys.Control).Consume);
        Assert.IsFalse(Resolve(Key.X, ModifierKeys.Control).Consume);
    }

    [TestMethod]
    public void ViewerShortcuts_IncludeMenuAndScreenPower()
    {
        Assert.AreEqual(ViewDeviceShortcutAction.Menu, Resolve(Key.M, ModifierKeys.Alt).Action);
        Assert.AreEqual(ViewDeviceShortcutAction.ScreenOff, Resolve(Key.O, ModifierKeys.Alt).Action);
        Assert.AreEqual(
            ViewDeviceShortcutAction.ScreenOn,
            Resolve(Key.O, ModifierKeys.Alt | ModifierKeys.Shift).Action);
        Assert.IsTrue(Resolve(Key.F, ModifierKeys.Alt).Consume);
        Assert.IsTrue(Resolve(Key.R, ModifierKeys.Alt).Consume);
    }

    [TestMethod]
    public void AltQ_IsConsumedWithoutAction()
    {
        ViewDeviceShortcutResolution result = Resolve(Key.Q, ModifierKeys.Alt);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.None, result.Action);
    }

    [TestMethod]
    public void AltShiftQ_IsConsumedWithoutAction()
    {
        ViewDeviceShortcutResolution result = Resolve(
            Key.Q,
            ModifierKeys.Alt | ModifierKeys.Shift);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.None, result.Action);
    }

    [TestMethod]
    public void AltH_IsConsumedAndHome()
    {
        ViewDeviceShortcutResolution result = Resolve(Key.H, ModifierKeys.Alt);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.Home, result.Action);
    }

    [TestMethod]
    public void AltUpRepeat_IsConsumedAndVolumeRepeatAllowed()
    {
        ViewDeviceShortcutResolution result = Resolve(
            Key.Up,
            ModifierKeys.Alt,
            isRepeat: true);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.VolumeUp, result.Action);
        Assert.IsTrue(ViewDeviceShortcutPolicy.AllowsRepeat(result.Action));
    }

    [TestMethod]
    public void AltPRepeat_IsConsumedButPowerNotRepeated()
    {
        ViewDeviceShortcutResolution result = Resolve(
            Key.P,
            ModifierKeys.Alt,
            isRepeat: true);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.Power, result.Action);
        Assert.IsFalse(ViewDeviceShortcutPolicy.AllowsRepeat(result.Action));
    }

    [TestMethod]
    public void PlainQ_IsNotConsumedByWindowShortcutResolver()
    {
        ViewDeviceShortcutResolution result = Resolve(Key.Q, ModifierKeys.None);

        Assert.IsFalse(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.None, result.Action);
    }

    [TestMethod]
    public void CtrlA_IsNotConsumedByViewerShortcutResolver()
    {
        ViewDeviceShortcutResolution result = Resolve(Key.A, ModifierKeys.Control);

        Assert.IsFalse(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.None, result.Action);
    }

    [TestMethod]
    public void AltN_FirstPressOpensNotifications_SecondOpensSettings()
    {
        ViewDeviceShortcutResolver resolver = new();

        Assert.AreEqual(
            ViewDeviceShortcutAction.ExpandNotifications,
            resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false).Action);
        Assert.AreEqual(
            ViewDeviceShortcutAction.ExpandSettings,
            resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false).Action);
        Assert.AreEqual(
            ViewDeviceShortcutAction.ExpandSettings,
            resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false).Action);
    }

    [TestMethod]
    public void AltN_RepeatIsConsumedWithoutAction()
    {
        ViewDeviceShortcutResolver resolver = new();
        resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false);

        ViewDeviceShortcutResolution result = resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: true);

        Assert.IsTrue(result.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.None, result.Action);
    }

    [TestMethod]
    public void VolumeShortcutRepeatPolicy_AllowsOnlyVolumeActions()
    {
        Assert.IsTrue(ViewDeviceShortcutPolicy.AllowsRepeat(ViewDeviceShortcutAction.VolumeUp));
        Assert.IsTrue(ViewDeviceShortcutPolicy.AllowsRepeat(ViewDeviceShortcutAction.VolumeDown));
        Assert.IsFalse(ViewDeviceShortcutPolicy.AllowsRepeat(ViewDeviceShortcutAction.Power));
        Assert.IsFalse(ViewDeviceShortcutPolicy.AllowsRepeat(ViewDeviceShortcutAction.Home));
        Assert.IsFalse(ViewDeviceShortcutPolicy.AllowsRepeat(ViewDeviceShortcutAction.Paste));
        Assert.IsFalse(ViewDeviceShortcutPolicy.AllowsRepeat(ViewDeviceShortcutAction.ScreenOn));
    }

    [TestMethod]
    public void AltN_ShiftCollapsesAndResetsSequence()
    {
        ViewDeviceShortcutResolver resolver = new();
        resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false);

        Assert.AreEqual(
            ViewDeviceShortcutAction.CollapsePanels,
            resolver.Resolve(Key.N, ModifierKeys.Alt | ModifierKeys.Shift, isRepeat: false).Action);
        Assert.AreEqual(
            ViewDeviceShortcutAction.ExpandNotifications,
            resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false).Action);
    }

    [TestMethod]
    public void AltN_AnotherViewerShortcutResetsSequence()
    {
        ViewDeviceShortcutResolver resolver = new();
        resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false);
        resolver.Resolve(Key.H, ModifierKeys.Alt, isRepeat: false);

        Assert.AreEqual(
            ViewDeviceShortcutAction.ExpandNotifications,
            resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false).Action);
    }

    [TestMethod]
    public void AltReleaseAndDeactivationResetSequence()
    {
        ViewDeviceShortcutResolver resolver = new();
        resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false);
        resolver.Reset();

        Assert.AreEqual(
            ViewDeviceShortcutAction.ExpandNotifications,
            resolver.Resolve(Key.N, ModifierKeys.Alt, isRepeat: false).Action);
    }

    [TestMethod]
    public void PrimaryPointerId_IsMinusOne()
    {
        Assert.IsTrue(object.Equals(ulong.MaxValue, ScrcpyDisplay.PrimaryPointerId));
    }

    [TestMethod]
    public void VirtualPointerId_RemainsMinusTwo()
    {
        Assert.IsTrue(object.Equals(ulong.MaxValue - 1, ScrcpyDisplay.VirtualPointerId));
    }

    [TestMethod]
    public void MirrorPoint_Center_RemainsCenter()
    {
        Point mirrored = ScrcpyDisplayGeometry.GetVirtualFingerPosition(
            new Point { X = 500, Y = 500 },
            1000,
            1000);

        Assert.AreEqual(500, mirrored.X);
        Assert.AreEqual(500, mirrored.Y);
    }

    [TestMethod]
    public void MirrorPoint_ProducesExpectedCoordinate()
    {
        Point mirrored = ScrcpyDisplayGeometry.GetVirtualFingerPosition(
            new Point { X = 120, Y = 340 },
            1080,
            2220);

        Assert.AreEqual(960, mirrored.X);
        Assert.AreEqual(1880, mirrored.Y);
    }

    [TestMethod]
    public void PointerDown_CaptureSuccess_CommitsPointer()
    {
        ScrcpyDisplayPointerState state = new();

        Assert.IsTrue(state.TryBeginPointerDown(captureSucceeded: true, pinchRequested: false));
        Assert.IsTrue(state.IsPointerDown);
        Assert.IsFalse(state.IsPinchActive);
    }

    [TestMethod]
    public void PointerDown_CaptureFailure_DoesNotCommit()
    {
        ScrcpyDisplayPointerState state = new();

        Assert.IsFalse(state.TryBeginPointerDown(captureSucceeded: false, pinchRequested: true));
        Assert.IsFalse(state.IsPointerDown);
        Assert.IsFalse(state.IsPinchActive);
    }

    [TestMethod]
    public void CaptureLoss_ActivePointer_RequiresRecoveryUp()
    {
        ScrcpyDisplayPointerState state = new();
        Assert.IsTrue(state.TryBeginPointerDown(true));

        Assert.IsTrue(state.ReleaseAfterCaptureLoss());
        Assert.IsFalse(state.IsPointerDown);
        Assert.IsFalse(state.ReleaseAfterCaptureLoss());
    }

    [TestMethod]
    public void CaptureLoss_NoPointer_NoMessage()
    {
        ScrcpyDisplayPointerState state = new();

        Assert.IsFalse(state.ReleaseAfterCaptureLoss());
    }

    [TestMethod]
    public void RecoveryUp_UsesZeroPressureAndNoButtons()
    {
        TouchEventControlMessage recovery = CreateTouch(
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
            ScrcpyDisplay.PrimaryPointerId,
            new Point { X = 10, Y = 20 });

        Assert.AreEqual(0f, recovery.Pressure);
        Assert.AreEqual((AndroidMotionEventButtons)0, recovery.Buttons);
    }

    [TestMethod]
    public void AfterRecoveryUp_NewPointerCanBeginNormally()
    {
        ScrcpyDisplayPointerState state = new();
        Assert.IsTrue(state.TryBeginPointerDown(true));
        Assert.IsTrue(state.ReleaseAfterCaptureLoss());

        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: false));
        Assert.IsTrue(state.IsPointerDown);
    }

    [TestMethod]
    public void CtrlLeftDown_StartsPinch()
    {
        ScrcpyDisplayPointerState state = new();

        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: true));
        Assert.IsTrue(state.IsPinchActive);
    }

    [TestMethod]
    public void NormalLeftDown_DoesNotStartPinch()
    {
        ScrcpyDisplayPointerState state = new();

        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: false));
        Assert.IsFalse(state.IsPinchActive);
    }

    [TestMethod]
    public void PinchMove_ProducesPrimaryAndVirtualMove()
    {
        Point primary = new() { X = 120, Y = 340 };
        Point virtualPoint = ScrcpyDisplayGeometry.GetVirtualFingerPosition(primary, 1080, 2220);

        TouchEventControlMessage primaryMove = CreateTouch(
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
            ScrcpyDisplay.PrimaryPointerId,
            primary);
        TouchEventControlMessage virtualMove = CreateTouch(
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
            ScrcpyDisplay.VirtualPointerId,
            virtualPoint);

        Assert.AreEqual(AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE, primaryMove.Action);
        Assert.AreEqual(AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE, virtualMove.Action);
        Assert.AreEqual(960, virtualMove.Position.Point.X);
        Assert.AreEqual(1880, virtualMove.Position.Point.Y);
    }

    [TestMethod]
    public void PinchUp_ReleasesBothPointers()
    {
        TouchEventControlMessage primaryUp = CreateTouch(
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
            ScrcpyDisplay.PrimaryPointerId,
            new Point { X = 120, Y = 340 });
        TouchEventControlMessage virtualUp = CreateTouch(
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
            ScrcpyDisplay.VirtualPointerId,
            new Point { X = 960, Y = 1880 });

        Assert.AreNotEqual(primaryUp.PointerId, virtualUp.PointerId);
        Assert.AreEqual(0f, primaryUp.Pressure);
        Assert.AreEqual(0f, virtualUp.Pressure);
        Assert.AreEqual((AndroidMotionEventButtons)0, primaryUp.Buttons);
        Assert.AreEqual((AndroidMotionEventButtons)0, virtualUp.Buttons);
    }

    [TestMethod]
    public void CaptureLoss_ReleasesBothPointersWithUp_NotCancel()
    {
        ScrcpyDisplayPointerState state = new();
        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: true));
        Assert.IsTrue(state.ReleaseAfterCaptureLoss());

        TouchEventControlMessage primary = CreateTouch(
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
            ScrcpyDisplay.PrimaryPointerId,
            new Point { X = 10, Y = 20 });
        TouchEventControlMessage virtualFinger = CreateTouch(
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
            ScrcpyDisplay.VirtualPointerId,
            new Point { X = 1070, Y = 2200 });

        Assert.AreEqual(AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP, primary.Action);
        Assert.AreEqual(AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP, virtualFinger.Action);
        Assert.AreNotEqual(AndroidMotionEventAction.AMOTION_EVENT_ACTION_CANCEL, primary.Action);
        Assert.AreNotEqual(AndroidMotionEventAction.AMOTION_EVENT_ACTION_CANCEL, virtualFinger.Action);
    }

    [TestMethod]
    public void VirtualDown_ButtonsZero_PressureOne()
    {
        Assert.AreEqual(
            (AndroidMotionEventButtons)0,
            ScrcpyDisplayTouchPolicy.GetButtons(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN,
                ScrcpyDisplay.VirtualPointerId));
        Assert.AreEqual(
            1f,
            ScrcpyDisplayTouchPolicy.GetPressure(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN));
    }

    [TestMethod]
    public void VirtualMove_ButtonsZero_PressureOne()
    {
        Assert.AreEqual(
            (AndroidMotionEventButtons)0,
            ScrcpyDisplayTouchPolicy.GetButtons(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                ScrcpyDisplay.VirtualPointerId));
        Assert.AreEqual(
            1f,
            ScrcpyDisplayTouchPolicy.GetPressure(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE));
    }

    [TestMethod]
    public void VirtualUp_ButtonsZero_PressureZero()
    {
        Assert.AreEqual(
            (AndroidMotionEventButtons)0,
            ScrcpyDisplayTouchPolicy.GetButtons(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                ScrcpyDisplay.VirtualPointerId));
        Assert.AreEqual(
            0f,
            ScrcpyDisplayTouchPolicy.GetPressure(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP));
    }

    [TestMethod]
    public void PrimaryDown_ButtonsPrimary()
    {
        Assert.AreEqual(
            AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY,
            ScrcpyDisplayTouchPolicy.GetButtons(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN,
                ScrcpyDisplay.PrimaryPointerId));
    }

    [TestMethod]
    public void PrimaryMove_ButtonsPrimary()
    {
        Assert.AreEqual(
            AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY,
            ScrcpyDisplayTouchPolicy.GetButtons(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                ScrcpyDisplay.PrimaryPointerId));
    }

    [TestMethod]
    public void PrimaryUp_ButtonsZero()
    {
        Assert.AreEqual(
            (AndroidMotionEventButtons)0,
            ScrcpyDisplayTouchPolicy.GetButtons(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                ScrcpyDisplay.PrimaryPointerId));
    }

    [TestMethod]
    public void CtrlReleasedDuringDrag_DoesNotEndPinch()
    {
        ScrcpyDisplayPointerState state = new();
        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: true));

        // There is deliberately no modifier-dependent transition after DOWN.
        Assert.IsTrue(state.IsPointerDown);
        Assert.IsTrue(state.IsPinchActive);
    }

    [TestMethod]
    public void NewPointerAfterCaptureLoss_StartsCleanly()
    {
        ScrcpyDisplayPointerState state = new();
        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: true));
        Assert.IsTrue(state.ReleaseAfterCaptureLoss());
        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: false));
        Assert.IsFalse(state.IsPinchActive);
    }

    [TestMethod]
    public void FirstKeyDown_RepeatZero()
    {
        ScrcpyDisplayKeyState state = new();

        Assert.AreEqual(0U, state.GetRepeatForKeyDown(Key.A, isRepeat: false));
    }

    [TestMethod]
    public void A_Down_TracksKey()
    {
        ScrcpyDisplayKeyState state = new();

        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        Assert.AreEqual(1, state.Count);
        Assert.IsTrue(state.TryRemove(Key.A, out ActiveAndroidKey activeKey));
        Assert.AreEqual(AndroidKeycode.AKEYCODE_A, activeKey.AndroidKeyCode);
    }

    [TestMethod]
    public void FirstRepeatedKeyDown_RepeatOne()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        Assert.AreEqual(1U, state.GetRepeatForKeyDown(Key.A, isRepeat: true));
    }

    [TestMethod]
    public void A_Repeat_IncrementsRepeat()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        uint repeat = state.GetRepeatForKeyDown(Key.A, isRepeat: true);
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat);

        Assert.AreEqual(2U, state.GetRepeatForKeyDown(Key.A, isRepeat: true));
    }

    [TestMethod]
    public void SecondRepeatedKeyDown_RepeatTwo()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        uint firstRepeat = state.GetRepeatForKeyDown(Key.A, isRepeat: true);
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            firstRepeat);
        Assert.AreEqual(2U, state.GetRepeatForKeyDown(Key.A, isRepeat: true));
    }

    [TestMethod]
    public void A_Up_RemovesTrackedKey()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        Assert.IsTrue(state.TryRemove(Key.A, out _));
        Assert.AreEqual(0, state.Count);
        Assert.IsFalse(state.TryRemove(Key.A, out _));
    }

    [TestMethod]
    public void UnknownKeyUp_DoesNotSendAndroidUp()
    {
        ScrcpyDisplayKeyState state = new();

        Assert.IsFalse(state.TryRemove(Key.F, out _));
        Assert.AreEqual(0, state.Count);
    }

    [TestMethod]
    public void KeyUp_ClearsRepeatState()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat: 1);
        Assert.IsTrue(state.TryRemove(Key.A, out _));

        Assert.AreEqual(0U, state.GetRepeatForKeyDown(Key.A, isRepeat: true));
    }

    [TestMethod]
    public void NewPressAfterKeyUp_ReturnsRepeatZero()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);
        Assert.IsTrue(state.TryRemove(Key.A, out _));

        Assert.AreEqual(0U, state.GetRepeatForKeyDown(Key.A, isRepeat: false));
    }

    [TestMethod]
    public void TwoDifferentKeys_MaintainIndependentRepeatState()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);
        state.TrackKeyDown(
            Key.B,
            AndroidKeycode.AKEYCODE_B,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        Assert.AreEqual(1U, state.GetRepeatForKeyDown(Key.A, isRepeat: true));
        Assert.AreEqual(1U, state.GetRepeatForKeyDown(Key.B, isRepeat: true));
        Assert.AreEqual(1U, state.GetRepeatForKeyDown(Key.A, isRepeat: true));
    }

    [TestMethod]
    public void ViewerShortcutRepeat_ThenKeyUp_ReleasesPreviouslyForwardedKey()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.H,
            AndroidKeycode.AKEYCODE_H,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        ViewDeviceShortcutResolution shortcut = Resolve(
            Key.H,
            ModifierKeys.Alt,
            isRepeat: true);
        Assert.IsTrue(shortcut.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.Home, shortcut.Action);

        Assert.IsTrue(state.TryRemove(Key.H, out ActiveAndroidKey activeKey));
        KeycodeControlMessage release = ScrcpyDisplay.CreateKeyReleaseMessage(
            activeKey,
            KeycodeHelper.ConvertModifiers(false, false, false, false));

        Assert.AreEqual(AndroidKeycode.AKEYCODE_H, release.KeyCode);
        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP, release.Action);
        Assert.AreEqual(AndroidMetastate.AMETA_NONE, release.Metastate);
        Assert.AreEqual(0, state.Count);
    }

    [TestMethod]
    public void PureAltHShortcut_ThenKeyUp_DoesNotProduceOrphanAndroidUp()
    {
        ScrcpyDisplayKeyState state = new();

        ViewDeviceShortcutResolution shortcut = Resolve(Key.H, ModifierKeys.Alt);

        Assert.IsTrue(shortcut.Consume);
        Assert.AreEqual(ViewDeviceShortcutAction.Home, shortcut.Action);
        Assert.IsFalse(state.TryRemove(Key.H, out _));
        Assert.AreEqual(0, state.Count);
    }

    [TestMethod]
    public void CtrlHeld_A_Down_HasCtrlMeta()
    {
        KeycodeControlMessage message = ScrcpyDisplay.CreateKeyDownMessage(
            AndroidKeycode.AKEYCODE_A,
            KeycodeHelper.ConvertModifiers(
                leftCtrl: true,
                rightCtrl: false,
                leftShift: false,
                rightShift: false),
            repeat: 0);

        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN, message.Action);
        Assert.AreEqual(
            AndroidMetastate.AMETA_CTRL_LEFT_ON | AndroidMetastate.AMETA_CTRL_ON,
            message.Metastate);
    }

    [TestMethod]
    public void CtrlReleasedBefore_A_Up_AUpDoesNotHaveCtrlMeta()
    {
        ActiveAndroidKey activeKey = new(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_CTRL_ON,
            Repeat: 0);

        KeycodeControlMessage release = ScrcpyDisplay.CreateKeyReleaseMessage(
            activeKey,
            KeycodeHelper.ConvertModifiers(
                leftCtrl: false,
                rightCtrl: false,
                leftShift: false,
                rightShift: false));

        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP, release.Action);
        Assert.AreEqual(AndroidMetastate.AMETA_NONE, release.Metastate);
    }

    [TestMethod]
    public void CtrlStillHeld_A_UpHasCtrlMeta()
    {
        ActiveAndroidKey activeKey = new(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            Repeat: 0);

        KeycodeControlMessage release = ScrcpyDisplay.CreateKeyReleaseMessage(
            activeKey,
            KeycodeHelper.ConvertModifiers(
                leftCtrl: true,
                rightCtrl: false,
                leftShift: false,
                rightShift: false));

        Assert.AreEqual(
            AndroidMetastate.AMETA_CTRL_LEFT_ON | AndroidMetastate.AMETA_CTRL_ON,
            release.Metastate);
    }

    [TestMethod]
    public void ForcedRelease_UsesStoredMetaState()
    {
        ActiveAndroidKey activeKey = new(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_CTRL_LEFT_ON | AndroidMetastate.AMETA_CTRL_ON,
            Repeat: 0);

        KeycodeControlMessage release = (KeycodeControlMessage)
            ScrcpyDisplay.CreateKeyReleaseMessages([activeKey])[0];

        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP, release.Action);
        Assert.AreEqual(
            AndroidMetastate.AMETA_CTRL_LEFT_ON | AndroidMetastate.AMETA_CTRL_ON,
            release.Metastate);
    }

    [TestMethod]
    public void KeyDown_ActionIsExplicitlyDown()
    {
        KeycodeControlMessage message = ScrcpyDisplay.CreateKeyDownMessage(
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_NONE,
            repeat: 0);

        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN, message.Action);
    }

    [TestMethod]
    public void FocusLoss_WithOneHeldKey_SendsOneUp()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(
            Key.A,
            AndroidKeycode.AKEYCODE_A,
            AndroidMetastate.AMETA_CTRL_ON,
            repeat: 2);

        IReadOnlyList<ActiveAndroidKey> active = state.SnapshotAndClear();
        IReadOnlyList<IControlMessage> releases = ScrcpyDisplay.CreateKeyReleaseMessages(active);
        ScrcpyControlCommandBatch batch = new(releases);

        Assert.AreEqual(1, batch.MessageCount);
        Assert.IsFalse(batch.IsDroppable);
        Assert.AreEqual(ControlMessageType.InjectKeycode, batch.MessageTypes[0]);
        Assert.AreEqual(
            (byte)AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
            batch.Payload.Span[1]);
    }

    [TestMethod]
    public void FocusLoss_WithMultipleHeldKeys_SendsOneCriticalBatch()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, AndroidMetastate.AMETA_NONE, 0);
        state.TrackKeyDown(Key.B, AndroidKeycode.AKEYCODE_B, AndroidMetastate.AMETA_SHIFT_ON, 1);

        IReadOnlyList<ActiveAndroidKey> active = state.SnapshotAndClear();
        IReadOnlyList<IControlMessage> releases = ScrcpyDisplay.CreateKeyReleaseMessages(active);
        ScrcpyControlCommandBatch batch = new(releases);

        Assert.AreEqual(2, batch.MessageCount);
        Assert.IsFalse(batch.IsDroppable);
        CollectionAssert.AreEqual(
            new[] { ControlMessageType.InjectKeycode, ControlMessageType.InjectKeycode },
            batch.MessageTypes.ToArray());
    }

    [TestMethod]
    public void FocusLoss_CtrlAndA_ReleasesABeforeCtrl()
    {
        ScrcpyDisplayKeyState state = new();
        AndroidMetastate ctrlMeta = KeycodeHelper.ConvertModifiers(true, false, false, false);
        state.TrackKeyDown(Key.LeftCtrl, AndroidKeycode.AKEYCODE_CTRL_LEFT, ctrlMeta, 0);
        state.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, ctrlMeta, 0);

        IReadOnlyList<IControlMessage> releases =
            ScrcpyDisplay.CreateKeyReleaseMessages(state.SnapshotAndClear());

        Assert.HasCount(2, releases);
        Assert.AreEqual(
            AndroidKeycode.AKEYCODE_A,
            ((KeycodeControlMessage)releases[0]).KeyCode);
        Assert.AreEqual(
            AndroidKeycode.AKEYCODE_CTRL_LEFT,
            ((KeycodeControlMessage)releases[1]).KeyCode);
        Assert.AreEqual(
            AndroidMetastate.AMETA_NONE,
            ((KeycodeControlMessage)releases[1]).Metastate);
    }

    [TestMethod]
    public void FocusLoss_ShiftAndA_ReleasesABeforeShift()
    {
        ScrcpyDisplayKeyState state = new();
        AndroidMetastate shiftMeta = KeycodeHelper.ConvertModifiers(false, false, true, false);
        state.TrackKeyDown(Key.LeftShift, AndroidKeycode.AKEYCODE_SHIFT_LEFT, shiftMeta, 0);
        state.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, shiftMeta, 0);

        IReadOnlyList<IControlMessage> releases =
            ScrcpyDisplay.CreateKeyReleaseMessages(state.SnapshotAndClear());

        Assert.HasCount(2, releases);
        Assert.AreEqual(
            AndroidKeycode.AKEYCODE_A,
            ((KeycodeControlMessage)releases[0]).KeyCode);
        Assert.AreEqual(
            AndroidKeycode.AKEYCODE_SHIFT_LEFT,
            ((KeycodeControlMessage)releases[1]).KeyCode);
        Assert.AreEqual(
            AndroidMetastate.AMETA_NONE,
            ((KeycodeControlMessage)releases[1]).Metastate);
    }

    [TestMethod]
    public void FocusLoss_CtrlShiftA_ReleasesLetterBeforeModifiers()
    {
        ScrcpyDisplayKeyState state = new();
        AndroidMetastate meta = KeycodeHelper.ConvertModifiers(true, false, true, false);
        state.TrackKeyDown(Key.LeftCtrl, AndroidKeycode.AKEYCODE_CTRL_LEFT, meta, 0);
        state.TrackKeyDown(Key.LeftShift, AndroidKeycode.AKEYCODE_SHIFT_LEFT, meta, 0);
        state.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, meta, 0);

        IReadOnlyList<IControlMessage> releases =
            ScrcpyDisplay.CreateKeyReleaseMessages(state.SnapshotAndClear());

        Assert.HasCount(3, releases);
        Assert.AreEqual(
            AndroidKeycode.AKEYCODE_A,
            ((KeycodeControlMessage)releases[0]).KeyCode);
        Assert.AreEqual(
            AndroidKeycode.AKEYCODE_SHIFT_LEFT,
            ((KeycodeControlMessage)releases[1]).KeyCode);
        Assert.AreEqual(
            AndroidKeycode.AKEYCODE_CTRL_LEFT,
            ((KeycodeControlMessage)releases[2]).KeyCode);
        Assert.AreEqual(
            AndroidMetastate.AMETA_CTRL_LEFT_ON | AndroidMetastate.AMETA_CTRL_ON,
            ((KeycodeControlMessage)releases[1]).Metastate);
        Assert.AreEqual(
            AndroidMetastate.AMETA_NONE,
            ((KeycodeControlMessage)releases[2]).Metastate);
    }

    [TestMethod]
    public void ForcedRelease_AllKeysEmittedExactlyOnce()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(Key.B, AndroidKeycode.AKEYCODE_B, AndroidMetastate.AMETA_NONE, 0);
        state.TrackKeyDown(
            Key.RightCtrl,
            AndroidKeycode.AKEYCODE_CTRL_RIGHT,
            KeycodeHelper.ConvertModifiers(false, true, false, false),
            0);
        state.TrackKeyDown(
            Key.LeftShift,
            AndroidKeycode.AKEYCODE_SHIFT_LEFT,
            KeycodeHelper.ConvertModifiers(false, false, true, false),
            0);
        state.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, AndroidMetastate.AMETA_NONE, 0);

        IReadOnlyList<IControlMessage> releases =
            ScrcpyDisplay.CreateKeyReleaseMessages(state.SnapshotAndClear());
        AndroidKeycode[] keyCodes = releases
            .Cast<KeycodeControlMessage>()
            .Select(message => message.KeyCode)
            .ToArray();

        Assert.HasCount(4, keyCodes);
        CollectionAssert.AreEquivalent(
            new[]
            {
                AndroidKeycode.AKEYCODE_A,
                AndroidKeycode.AKEYCODE_B,
                AndroidKeycode.AKEYCODE_SHIFT_LEFT,
                AndroidKeycode.AKEYCODE_CTRL_RIGHT
            },
            keyCodes);
        Assert.AreEqual(4, keyCodes.Distinct().Count());
    }

    [TestMethod]
    public void ForcedRelease_StateClearedBeforeSend()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, AndroidMetastate.AMETA_NONE, 0);

        IReadOnlyList<ActiveAndroidKey> activeKeys = state.SnapshotAndClear();

        Assert.AreEqual(0, state.Count);
        Assert.HasCount(1, ScrcpyDisplay.CreateKeyReleaseMessages(activeKeys));
    }

    [TestMethod]
    public void RejectedForcedRelease_DoesNotRepopulateLocalState()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, AndroidMetastate.AMETA_NONE, 0);
        IReadOnlyList<ActiveAndroidKey> activeKeys = state.SnapshotAndClear();
        IReadOnlyList<IControlMessage> releases =
            ScrcpyDisplay.CreateKeyReleaseMessages(activeKeys);

        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        for (int i = 0; i < ScrcpyControlCommandQueue.Capacity; i++)
            Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteBatchDetailed(releases));
        Assert.AreEqual(0, state.Count);
        queue.EndSession();
    }

    [TestMethod]
    public void FocusLoss_ClearsRepeatState()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, AndroidMetastate.AMETA_NONE, 3);

        Assert.HasCount(1, state.SnapshotAndClear());
        Assert.AreEqual(0, state.Count);
        Assert.AreEqual(0U, state.GetRepeatForKeyDown(Key.A, isRepeat: true));
    }

    [TestMethod]
    public void ClientReplacement_ReleasesKeysAgainstOldClient()
    {
        ScrcpyDisplayKeyState oldState = new();
        oldState.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, AndroidMetastate.AMETA_NONE, 0);
        IReadOnlyList<ActiveAndroidKey> oldKeys = oldState.SnapshotAndClear();

        IReadOnlyList<IControlMessage> oldReleases = ScrcpyDisplay.CreateKeyReleaseMessages(oldKeys);
        ScrcpyControlCommandBatch oldBatch = new(oldReleases);
        ScrcpyDisplayKeyState newState = new();

        Assert.AreEqual(ControlMessageType.InjectKeycode, oldBatch.MessageTypes[0]);
        Assert.AreEqual(
            (int)AndroidKeycode.AKEYCODE_A,
            BinaryPrimitives.ReadInt32BigEndian(oldBatch.Payload.Span[2..6]));
        Assert.AreEqual(0, newState.Count);
    }

    [TestMethod]
    public void NewClient_StartsWithEmptyKeyState()
    {
        ScrcpyDisplayKeyState state = new();
        state.TrackKeyDown(Key.A, AndroidKeycode.AKEYCODE_A, AndroidMetastate.AMETA_NONE, 0);
        _ = state.SnapshotAndClear();

        ScrcpyDisplayKeyState newClientState = new();
        Assert.AreEqual(0, newClientState.Count);
    }

    [TestMethod]
    public void NewSession_IsEmpty()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();

        Assert.IsFalse(session.TryRead(out _));
    }

    [TestMethod]
    public void OldSessionCommands_DoNotReplay()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession oldSession = queue.StartSession();
        Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));

        ScrcpyControlQueueSession newSession = queue.StartSession();
        Assert.IsFalse(newSession.TryRead(out _));
        Assert.IsTrue(oldSession.TryRead(out _));
    }

    [TestMethod]
    public void Capacity_Is64()
    {
        Assert.IsTrue(object.Equals(64, ScrcpyControlCommandQueue.Capacity));
    }

    [TestMethod]
    public void DroppableMove_BeyondSoftLimitIsRejectedWithoutCorruptingQueue()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        for (int i = 0; i < ScrcpyControlCommandQueue.DroppableSoftLimit; i++)
            Assert.IsTrue(queue.TryWrite(CreateMove(i)));

        IControlMessage rejected = CreateMove(ScrcpyControlCommandQueue.DroppableSoftLimit + 1);
        Assert.IsTrue(ScrcpyControlMessagePolicy.IsDroppable(rejected));
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteDetailed(rejected));
        Assert.IsTrue(session.TryRead(out ScrcpyControlQueueItem? firstItem));
        ScrcpyControlCommandBatch firstBatch = ((ScrcpyControlQueueItem.Batch)firstItem!).Value;
        Assert.AreEqual(1, firstBatch.MessageCount);
        Assert.AreEqual(ControlMessageType.InjectScrollEvent, firstBatch.MessageTypes[0]);
    }

    [TestMethod]
    public void CriticalMessageAtCapacity_IsIdentifiedAsCriticalFailure()
    {
        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        for (int i = 0; i < ScrcpyControlCommandQueue.Capacity; i++)
            Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));

        KeycodeControlMessage critical = new();
        Assert.IsFalse(ScrcpyControlMessagePolicy.IsDroppable(critical));
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteDetailed(critical));
    }

    [TestMethod]
    public void EndSession_RejectsWrites()
    {
        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        queue.EndSession();

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Unavailable,
            queue.TryWriteDetailed(new CollapsePanelsControlMessage()));
        Assert.IsFalse(queue.TryWrite(new CollapsePanelsControlMessage()));
    }

    [TestMethod]
    public void Restart_GetsFreshEmptyQueue()
    {
        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));
        queue.EndSession();

        ScrcpyControlQueueSession session = queue.StartSession();
        Assert.IsFalse(session.TryRead(out _));
    }

    [TestMethod]
    public void OneFrame_SchedulesOneRender()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);

        Assert.IsTrue(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1)));
        Assert.IsFalse(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(2)));
    }

    [TestMethod]
    public void TenFramesBeforeUiRuns_StillSchedulesOne()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);

        Assert.IsTrue(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1)));
        for (int i = 2; i <= 10; i++)
            Assert.IsFalse(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame((byte)i)));
    }

    [TestMethod]
    public void TenFramesBeforeUiRuns_KeepsOnlyLatest()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        for (int i = 1; i <= 10; i++)
            mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame((byte)i));

        Assert.IsTrue(mailbox.TryTake(out ScrcpyDisplayFrameMailbox.PendingFrame frame));
        Assert.AreEqual(10, frame.Buffer![0]);
        mailbox.Return(ref frame);
        Assert.IsFalse(mailbox.CompleteRenderAndCheckPending());
    }

    [TestMethod]
    public void ReplacingPendingFrame_ReturnsOldBuffer()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(2));

        Assert.AreEqual(1, pool.ReturnCount);
    }

    [TestMethod]
    public void Consume_ReturnsRenderedBufferExactlyOnce()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));
        Assert.IsTrue(mailbox.TryTake(out ScrcpyDisplayFrameMailbox.PendingFrame frame));

        mailbox.Return(ref frame);
        mailbox.Return(ref frame);
        Assert.AreEqual(1, pool.ReturnCount);
    }

    [TestMethod]
    public void NewGeneration_DropsOldPendingFrame()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder oldDecoder);
        VideoStreamDecoder newDecoder = CreateDecoder();
        mailbox.TryPublish(oldDecoder, 1, 2, 2, CreateFrame(1));
        mailbox.SetCurrentSource(newDecoder, 2);

        Assert.IsFalse(mailbox.TryTake(out _));
        Assert.AreEqual(1, pool.ReturnCount);
    }

    [TestMethod]
    public void OldDecoderFrame_IsRejected()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder currentDecoder);
        VideoStreamDecoder oldDecoder = CreateDecoder();
        Assert.IsFalse(mailbox.TryPublish(oldDecoder, 1, 2, 2, CreateFrame(1)));
        Assert.IsFalse(mailbox.TryTake(out _));
        Assert.AreEqual(1, pool.ReturnCount);

        _ = currentDecoder;
    }

    [TestMethod]
    public void FrameArrivesDuringRender_SchedulesExactlyOneMoreCallback()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        Assert.IsTrue(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1)));
        Assert.IsTrue(mailbox.TryTake(out ScrcpyDisplayFrameMailbox.PendingFrame first));

        Assert.IsFalse(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(2)));
        mailbox.Return(ref first);
        Assert.IsTrue(mailbox.CompleteRenderAndCheckPending());
        Assert.IsTrue(mailbox.TryTake(out ScrcpyDisplayFrameMailbox.PendingFrame second));
        mailbox.Return(ref second);
        Assert.IsFalse(mailbox.CompleteRenderAndCheckPending());
    }

    [TestMethod]
    public void RenderedPortrait_DecoderAlreadyLandscape_InputUsesPortrait()
    {
        Assert.IsTrue(ScrcpyDisplay.TryMapPointToRenderedFrame(
            new System.Windows.Point(400, 400),
            renderedWidth: 1080,
            renderedHeight: 2220,
            targetWidth: 800,
            targetHeight: 800,
            out System.Windows.Point mapped));

        Assert.AreEqual(new System.Windows.Point(540, 1110), mapped);
    }

    [TestMethod]
    public void AfterLandscapeActuallyRendered_InputUsesLandscape()
    {
        Assert.IsTrue(ScrcpyDisplay.TryMapPointToRenderedFrame(
            new System.Windows.Point(400, 400),
            renderedWidth: 2220,
            renderedHeight: 1080,
            targetWidth: 800,
            targetHeight: 800,
            out System.Windows.Point mapped));

        Assert.AreEqual(new System.Windows.Point(1110, 540), mapped);
    }

    [TestMethod]
    public void NoRenderedFrame_InputRejected()
    {
        Assert.IsFalse(ScrcpyDisplay.TryMapPointToRenderedFrame(
            new System.Windows.Point(400, 400),
            renderedWidth: 0,
            renderedHeight: 0,
            targetWidth: 800,
            targetHeight: 800,
            out _));
    }

    [TestMethod]
    public void SizeChangeWhilePrimaryDown_SendsOldCoordinateUp()
    {
        ScrcpyDisplayPointerState state = new();
        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: false));
        Assert.IsTrue(ScrcpyDisplay.ShouldRecoverPointerForFrameSizeChange(
            renderedWidth: 1080,
            renderedHeight: 2220,
            nextWidth: 2220,
            nextHeight: 1080,
            pointerIsActive: state.IsPointerDown));
        Position oldPosition = new()
        {
            Point = new Point { X = 120, Y = 340 },
            ScreenSize = new ScreenSize { Width = 1080, Height = 2220 }
        };
        IReadOnlyList<TouchEventControlMessage> messages =
            ScrcpyDisplay.CreatePointerRecoveryMessages(oldPosition, state.IsPinchActive);
        Assert.IsTrue(state.ReleaseAfterCaptureLoss());
        ScrcpyControlCommandBatch batch = new(messages.Cast<IControlMessage>().ToArray());
        Assert.AreEqual(1, batch.MessageCount);
        Assert.IsFalse(batch.IsDroppable);
        Assert.AreEqual(ControlMessageType.InjectTouchEvent, batch.MessageTypes[0]);
        Assert.AreEqual((byte)AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP, batch.Payload.Span[1]);
        Assert.AreEqual(oldPosition.Point.X, BinaryPrimitives.ReadInt32BigEndian(batch.Payload.Span[10..14]));
        Assert.AreEqual(oldPosition.Point.Y, BinaryPrimitives.ReadInt32BigEndian(batch.Payload.Span[14..18]));
        Assert.AreEqual(oldPosition.ScreenSize.Width, BinaryPrimitives.ReadUInt16BigEndian(batch.Payload.Span[18..20]));
        Assert.AreEqual(oldPosition.ScreenSize.Height, BinaryPrimitives.ReadUInt16BigEndian(batch.Payload.Span[20..22]));
    }
    [TestMethod]
    public void SizeChangeWhilePinchDown_SendsBothOldCoordinateUps()
    {
        ScrcpyDisplayPointerState state = new();
        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: true));
        Assert.IsTrue(ScrcpyDisplay.ShouldRecoverPointerForFrameSizeChange(
            renderedWidth: 1080,
            renderedHeight: 2220,
            nextWidth: 2220,
            nextHeight: 1080,
            pointerIsActive: state.IsPointerDown));
        Position oldPosition = new()
        {
            Point = new Point { X = 120, Y = 340 },
            ScreenSize = new ScreenSize { Width = 1080, Height = 2220 }
        };
        IReadOnlyList<TouchEventControlMessage> messages =
            ScrcpyDisplay.CreatePointerRecoveryMessages(oldPosition, state.IsPinchActive);
        Assert.IsTrue(state.ReleaseAfterCaptureLoss());
        ScrcpyControlCommandBatch batch = new(messages.Cast<IControlMessage>().ToArray());
        Assert.AreEqual(2, batch.MessageCount);
        Assert.IsFalse(batch.IsDroppable);
        Assert.AreEqual((byte)AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP, batch.Payload.Span[1]);
        Assert.AreEqual((byte)AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP, batch.Payload.Span[29]);
        Assert.AreEqual(oldPosition.Point.X, BinaryPrimitives.ReadInt32BigEndian(batch.Payload.Span[10..14]));
        Assert.AreEqual(oldPosition.Point.Y, BinaryPrimitives.ReadInt32BigEndian(batch.Payload.Span[14..18]));
        Assert.AreEqual(960, BinaryPrimitives.ReadInt32BigEndian(batch.Payload.Span[38..42]));
        Assert.AreEqual(1880, BinaryPrimitives.ReadInt32BigEndian(batch.Payload.Span[42..46]));
        Assert.AreEqual(oldPosition.ScreenSize.Width, BinaryPrimitives.ReadUInt16BigEndian(batch.Payload.Span[46..48]));
        Assert.AreEqual(oldPosition.ScreenSize.Height, BinaryPrimitives.ReadUInt16BigEndian(batch.Payload.Span[48..50]));
    }
    [TestMethod]
    public void SizeChangeRelease_DoesNotDoubleSendOnLostCapture()
    {
        ScrcpyDisplayPointerState state = new();
        Assert.IsTrue(state.TryBeginPointerDown(true, pinchRequested: true));
        Assert.IsTrue(state.ReleaseAfterCaptureLoss());
        Assert.IsFalse(state.ReleaseAfterCaptureLoss());
        Assert.IsFalse(state.IsPointerDown);
        Assert.IsFalse(state.IsPinchActive);
    }
    [TestMethod]
    public void RenderException_CompletesMailboxAndReturnsBufferExactlyOnce()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        Assert.IsTrue(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1)));
        bool renderStarted = false;
        bool publishedDuringRender = false;
        bool scheduleAgain = ScrcpyDisplay.ProcessPendingFrame(
            mailbox,
            frame =>
            {
                renderStarted = true;
                publishedDuringRender = mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(2));
                throw new InvalidOperationException("simulated renderer failure");
            });
        Assert.IsTrue(renderStarted);
        Assert.IsFalse(publishedDuringRender);
        Assert.IsTrue(scheduleAgain);
        Assert.AreEqual(1, pool.ReturnCount);
        bool finalSchedule = ScrcpyDisplay.ProcessPendingFrame(
            mailbox,
            static _ => { });
        Assert.IsFalse(finalSchedule);
        Assert.AreEqual(2, pool.ReturnCount);
    }

    [TestMethod]
    public void RecoverableRenderException_ReturnsCurrentFrameOnce()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));

        bool scheduleAgain = ScrcpyDisplay.ProcessPendingFrame(
            mailbox,
            static _ => throw new InvalidOperationException("recoverable render failure"));

        Assert.IsFalse(scheduleAgain);
        Assert.AreEqual(1, pool.ReturnCount);
        Assert.IsFalse(mailbox.TryTake(out _));
    }

    [TestMethod]
    public void RecoverableRenderException_AllowsFutureFrameScheduling()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));

        _ = ScrcpyDisplay.ProcessPendingFrame(
            mailbox,
            static _ => throw new InvalidOperationException("recoverable render failure"));

        Assert.IsTrue(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(2)));
        Assert.IsFalse(ScrcpyDisplay.ProcessPendingFrame(mailbox, static _ => { }));
        Assert.AreEqual(2, pool.ReturnCount);
    }

    [TestMethod]
    public void FrameAfterRecoverableFailure_CanScheduleNormally()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));

        _ = ScrcpyDisplay.ProcessPendingFrame(
            mailbox,
            static _ => throw new InvalidOperationException("recoverable render failure"));

        Assert.IsTrue(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(2)));
        Assert.IsFalse(ScrcpyDisplay.ProcessPendingFrame(mailbox, static _ => { }));
        Assert.AreEqual(2, pool.ReturnCount);
    }

    [TestMethod]
    public void OomRenderException_ReturnsCurrentFrameOnce()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));

        Assert.ThrowsExactly<OutOfMemoryException>(
            () => ScrcpyDisplay.ProcessPendingFrame(
                mailbox,
                static _ => throw new OutOfMemoryException("synthetic render OOM")));

        Assert.AreEqual(1, pool.ReturnCount);
    }

    [TestMethod]
    public void OomRenderException_ReturnsPendingFrameOnce()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));

        Assert.ThrowsExactly<OutOfMemoryException>(
            () => ScrcpyDisplay.ProcessPendingFrame(
                mailbox,
                _ =>
                {
                    Assert.IsFalse(mailbox.TryPublish(
                        decoder,
                        1,
                        2,
                        2,
                        CreateFrame(2)));
                    throw new OutOfMemoryException("synthetic render OOM");
                }));

        Assert.AreEqual(2, pool.ReturnCount);
    }

    [TestMethod]
    public void OomRenderException_LeavesNoPendingFrame()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));

        Assert.ThrowsExactly<OutOfMemoryException>(
            () => ScrcpyDisplay.ProcessPendingFrame(
                mailbox,
                _ =>
                {
                    mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(2));
                    throw new OutOfMemoryException("synthetic render OOM");
                }));

        Assert.IsFalse(mailbox.TryTake(out _));
        Assert.AreEqual(2, pool.ReturnCount);
    }

    [TestMethod]
    public void OomRenderException_RethrowsSameException()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));
        OutOfMemoryException expected = new("synthetic render OOM");

        OutOfMemoryException thrown = Assert.ThrowsExactly<OutOfMemoryException>(
            () => ScrcpyDisplay.ProcessPendingFrame(mailbox, _ => throw expected));

        Assert.AreSame(expected, thrown);
        Assert.AreEqual(1, pool.ReturnCount);
    }

    [TestMethod]
    public void OomRenderException_ReturnsCurrentAndPendingFramesOnce()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));
        OutOfMemoryException expected = new("synthetic render OOM");

        OutOfMemoryException thrown = Assert.ThrowsExactly<OutOfMemoryException>(
            () => ScrcpyDisplay.ProcessPendingFrame(
                mailbox,
                _ =>
                {
                    Assert.IsFalse(mailbox.TryPublish(
                        decoder,
                        1,
                        2,
                        2,
                        CreateFrame(2)));
                    throw expected;
                }));

        Assert.AreSame(expected, thrown);
        Assert.AreEqual(2, pool.ReturnCount);
        Assert.IsFalse(mailbox.TryTake(out _));
        Assert.IsTrue(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(3)));
    }

    [TestMethod]
    public void OomRenderException_LeavesRenderScheduledFalse()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);

        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));
        Assert.ThrowsExactly<OutOfMemoryException>(
            () => ScrcpyDisplay.ProcessPendingFrame(
                mailbox,
                static _ => throw new OutOfMemoryException("synthetic render OOM")));

        Assert.IsTrue(mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(2)));
    }

    [TestMethod]
    public void SourceReplacementDuringRender_DoesNotLeakBuffer()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder oldDecoder);
        VideoStreamDecoder newDecoder = CreateDecoder();
        mailbox.TryPublish(oldDecoder, 1, 2, 2, CreateFrame(1));

        bool scheduleAgain = ScrcpyDisplay.ProcessPendingFrame(
            mailbox,
            _ => mailbox.SetCurrentSource(newDecoder, 2));

        Assert.IsFalse(scheduleAgain);
        Assert.AreEqual(1, pool.ReturnCount);
        Assert.IsFalse(mailbox.TryTake(out _));
        Assert.IsTrue(mailbox.TryPublish(newDecoder, 2, 2, 2, CreateFrame(2)));
    }

    [TestMethod]
    public void MailboxClear_ReturnsPendingBuffer()
    {
        TrackingArrayPool pool = new();
        ScrcpyDisplayFrameMailbox mailbox = CreateMailbox(pool, out VideoStreamDecoder decoder);
        mailbox.TryPublish(decoder, 1, 2, 2, CreateFrame(1));
        mailbox.Clear();

        Assert.AreEqual(1, pool.ReturnCount);
        Assert.IsFalse(mailbox.TryTake(out _));
    }

    private static ViewDeviceShortcutResolution Resolve(
        Key physicalKey,
        ModifierKeys modifiers,
        bool isRepeat = false)
    {
        return new ViewDeviceShortcutResolver().Resolve(physicalKey, modifiers, isRepeat);
    }

    private static ScrcpyDisplayFrameMailbox CreateMailbox(
        TrackingArrayPool pool,
        out VideoStreamDecoder decoder)
    {
        decoder = CreateDecoder();
        ScrcpyDisplayFrameMailbox mailbox = new(pool);
        mailbox.SetCurrentSource(decoder, 1);
        return mailbox;
    }

    private static VideoStreamDecoder CreateDecoder()
    {
        VideoStreamDecoder decoder =
            (VideoStreamDecoder)RuntimeHelpers.GetUninitializedObject(typeof(VideoStreamDecoder));
        GC.SuppressFinalize(decoder);
        return decoder;
    }

    private static byte[] CreateFrame(byte value)
    {
        return Enumerable.Repeat(value, 16).Select(static value => (byte)value).ToArray();
    }

    private static TouchEventControlMessage CreateTouch(
        AndroidMotionEventAction action,
        ulong pointerId,
        Point point)
    {
        return new TouchEventControlMessage
        {
            Action = action,
            PointerId = pointerId,
            Position = new Position
            {
                Point = point,
                ScreenSize = new ScreenSize { Width = 1080, Height = 2220 }
            },
            Pressure = ScrcpyDisplayTouchPolicy.GetPressure(action),
            Buttons = ScrcpyDisplayTouchPolicy.GetButtons(action, pointerId)
        };
    }

    private static TouchEventControlMessage CreateTouch(
        AndroidMotionEventAction action,
        ulong pointerId,
        System.Windows.Point point)
    {
        return new TouchEventControlMessage
        {
            Action = action,
            PointerId = pointerId,
            Position = new Position
            {
                Point = new ScrcpyNet.Point { X = (int)point.X, Y = (int)point.Y },
                ScreenSize = new ScreenSize { Width = 1080, Height = 2220 }
            },
            Pressure = ScrcpyDisplayTouchPolicy.GetPressure(action),
            Buttons = ScrcpyDisplayTouchPolicy.GetButtons(action, pointerId)
        };
    }

    private static ScrollEventControlMessage CreateMove(int value)
    {
        return new ScrollEventControlMessage
        {
            Position = new Position
            {
                Point = new Point { X = value, Y = value },
                ScreenSize = new ScreenSize { Width = 1080, Height = 2220 }
            }
        };
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        public int ReturnCount { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            return new byte[minimumLength];
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            ReturnCount++;
        }
    }
}
