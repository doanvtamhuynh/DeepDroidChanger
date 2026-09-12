using Serilog;
using System.Collections.Generic;
using System.Windows.Input;

namespace ScrcpyNet.Wpf
{
    public static class KeycodeHelper
    {
        private static readonly Dictionary<Key, AndroidKeycode> keycodeDict = new Dictionary<Key, AndroidKeycode>
        {
            { Key.Space, AndroidKeycode.AKEYCODE_SPACE },
            { Key.Back, AndroidKeycode.AKEYCODE_DEL },
            { Key.Left, AndroidKeycode.AKEYCODE_DPAD_LEFT },
            { Key.Up, AndroidKeycode.AKEYCODE_DPAD_UP },
            { Key.Right, AndroidKeycode.AKEYCODE_DPAD_RIGHT },
            { Key.Down, AndroidKeycode.AKEYCODE_DPAD_DOWN },
            { Key.Delete, AndroidKeycode.AKEYCODE_FORWARD_DEL },
            { Key.Tab, AndroidKeycode.AKEYCODE_TAB },
            { Key.Enter, AndroidKeycode.AKEYCODE_ENTER },
            { Key.OemComma, AndroidKeycode.AKEYCODE_COMMA },
            { Key.OemPeriod, AndroidKeycode.AKEYCODE_PERIOD },
            { Key.Oem2, AndroidKeycode.AKEYCODE_SLASH },
            { Key.Oem1, AndroidKeycode.AKEYCODE_SEMICOLON },
            { Key.OemQuotes, AndroidKeycode.AKEYCODE_APOSTROPHE },
            { Key.OemMinus, AndroidKeycode.AKEYCODE_MINUS },
            { Key.OemPlus, AndroidKeycode.AKEYCODE_EQUALS },
            { Key.OemOpenBrackets, AndroidKeycode.AKEYCODE_LEFT_BRACKET },
            { Key.OemCloseBrackets, AndroidKeycode.AKEYCODE_RIGHT_BRACKET }
        };

        public static bool TryConvertKey(Key key, out AndroidKeycode androidKey)
        {
            // This maps physical WPF keys only; Unicode text and IME input are not implemented.
            // A - Z
            if (key >= Key.A && key <= Key.Z)
            {
                int offset = (int)AndroidKeycode.AKEYCODE_A - (int)Key.A;
                androidKey = (AndroidKeycode)((int)key + offset);
                return true;
            }

            // Digits 0-9
            if (key >= Key.D0 && key <= Key.D9)
            {
                int offset = (int)AndroidKeycode.AKEYCODE_0 - (int)Key.D0;
                androidKey = (AndroidKeycode)((int)key + offset);
                return true;
            }

            if (keycodeDict.TryGetValue(key, out androidKey))
                return true;

            Log.Debug("Unsupported physical key: {@Key}", key);
            androidKey = AndroidKeycode.AKEYCODE_UNKNOWN;
            return false;
        }

        public static AndroidKeycode ConvertKey(Key key)
        {
            return TryConvertKey(key, out AndroidKeycode androidKey)
                ? androidKey
                : AndroidKeycode.AKEYCODE_UNKNOWN;
        }

        public static AndroidMetastate ConvertModifiers(ModifierKeys keyModifiers)
        {
            AndroidMetastate metastate = AndroidMetastate.AMETA_NONE;

            if (keyModifiers.HasFlag(ModifierKeys.Shift))
                metastate |= AndroidMetastate.AMETA_SHIFT_ON;

            if (keyModifiers.HasFlag(ModifierKeys.Control))
                metastate |= AndroidMetastate.AMETA_CTRL_ON;

            if (keyModifiers.HasFlag(ModifierKeys.Alt))
                metastate |= AndroidMetastate.AMETA_ALT_ON;

            return metastate;
        }
    }
}
