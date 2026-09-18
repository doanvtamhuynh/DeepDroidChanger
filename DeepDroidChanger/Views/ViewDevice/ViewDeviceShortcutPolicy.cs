namespace DeepDroidChanger.Views;

internal static class ViewDeviceShortcutPolicy
{
    public static bool ShouldExecute(
        ViewDeviceShortcutResolution resolution,
        bool isRepeat)
    {
        return resolution.Consume &&
               (!isRepeat || AllowsRepeat(resolution.Action));
    }

    public static bool AllowsRepeat(ViewDeviceShortcutAction action)
    {
        return action is ViewDeviceShortcutAction.VolumeUp or
            ViewDeviceShortcutAction.VolumeDown;
    }
}
