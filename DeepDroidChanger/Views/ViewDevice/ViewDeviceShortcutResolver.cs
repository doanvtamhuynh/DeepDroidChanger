using System.Windows.Input;

namespace DeepDroidChanger.Views;

internal enum ViewDeviceShortcutAction
{
    None,
    Home,
    Back,
    Recent,
    VolumeUp,
    VolumeDown,
    Power,
    ScreenOff,
    ScreenOn,
    Copy,
    Cut,
    Paste,
    PasteWithPasteKey,
    InjectClipboardText,
    ExpandNotifications,
    ExpandSettings,
    CollapsePanels,
    Menu
}

internal readonly record struct ViewDeviceShortcutResolution(
    ViewDeviceShortcutAction Action,
    bool Consume);

internal sealed class ViewDeviceShortcutResolver
{
    private int altNPressCount;

    public ViewDeviceShortcutResolution Resolve(
        Key physicalKey,
        ModifierKeys modifiers,
        bool isRepeat)
    {
        bool isAlt = modifiers == ModifierKeys.Alt;
        bool isAltShift = modifiers == (ModifierKeys.Alt | ModifierKeys.Shift);

        if (physicalKey == Key.N && isAltShift)
        {
            altNPressCount = 0;
            return new(ViewDeviceShortcutAction.CollapsePanels, true);
        }

        if (physicalKey == Key.N && isAlt)
        {
            if (isRepeat)
                return new(ViewDeviceShortcutAction.None, true);

            altNPressCount = Math.Min(2, altNPressCount + 1);
            return new(
                altNPressCount == 1
                    ? ViewDeviceShortcutAction.ExpandNotifications
                    : ViewDeviceShortcutAction.ExpandSettings,
                true);
        }

        ViewDeviceShortcutResolution directAction = ResolveDirectClipboardShortcut(
            physicalKey,
            modifiers);
        if (directAction.Consume)
        {
            if ((modifiers & ModifierKeys.Alt) != 0)
                altNPressCount = 0;
            return directAction;
        }

        if ((modifiers & ModifierKeys.Alt) == 0)
            return new(ViewDeviceShortcutAction.None, false);

        // Any other Alt shortcut starts a new sequence for Alt+N.
        altNPressCount = 0;
        ViewDeviceShortcutAction action = (modifiers, physicalKey) switch
        {
            (ModifierKeys.Alt, Key.H) => ViewDeviceShortcutAction.Home,
            (ModifierKeys.Alt, Key.B) => ViewDeviceShortcutAction.Back,
            (ModifierKeys.Alt, Key.S) => ViewDeviceShortcutAction.Recent,
            (ModifierKeys.Alt, Key.Up) => ViewDeviceShortcutAction.VolumeUp,
            (ModifierKeys.Alt, Key.Down) => ViewDeviceShortcutAction.VolumeDown,
            (ModifierKeys.Alt, Key.P) => ViewDeviceShortcutAction.Power,
            (ModifierKeys.Alt, Key.M) => ViewDeviceShortcutAction.Menu,
            (ModifierKeys.Alt, Key.O) => ViewDeviceShortcutAction.ScreenOff,
            (ModifierKeys.Alt | ModifierKeys.Shift, Key.O) => ViewDeviceShortcutAction.ScreenOn,
            _ => ViewDeviceShortcutAction.None
        };

        // Alt is reserved for the Single View command namespace. Unknown Alt
        // combinations are consumed too, so they are never injected into
        // Android as ordinary key events.
        return new(action, true);
    }

    public void Reset()
    {
        altNPressCount = 0;
    }

    private static ViewDeviceShortcutResolution ResolveDirectClipboardShortcut(
        Key physicalKey,
        ModifierKeys modifiers)
    {
        return (modifiers, physicalKey) switch
        {
            (ModifierKeys.Control, Key.V) => new(ViewDeviceShortcutAction.Paste, true),
            (ModifierKeys.Alt, Key.V) => new(ViewDeviceShortcutAction.PasteWithPasteKey, true),
            (ModifierKeys.Alt | ModifierKeys.Shift, Key.V) =>
                new(ViewDeviceShortcutAction.InjectClipboardText, true),
            (ModifierKeys.Alt, Key.C) => new(ViewDeviceShortcutAction.Copy, true),
            (ModifierKeys.Alt, Key.X) => new(ViewDeviceShortcutAction.Cut, true),
            _ => new(ViewDeviceShortcutAction.None, false)
        };
    }
}
