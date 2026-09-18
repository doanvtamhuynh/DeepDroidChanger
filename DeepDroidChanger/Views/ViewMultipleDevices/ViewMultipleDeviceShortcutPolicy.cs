using System.Windows.Input;

namespace DeepDroidChanger.Views;

internal static class ViewMultipleDeviceShortcutPolicy
{
    internal static bool IsExactHostPaste(Key key, ModifierKeys modifiers)
    {
        return key == Key.V &&
               modifiers.HasFlag(ModifierKeys.Control) &&
               !modifiers.HasFlag(ModifierKeys.Shift) &&
               !modifiers.HasFlag(ModifierKeys.Alt);
    }

    internal static bool ShouldExecuteHostPaste(
        Key key,
        ModifierKeys modifiers,
        bool isRepeat)
    {
        return IsExactHostPaste(key, modifiers) && !isRepeat;
    }
}
