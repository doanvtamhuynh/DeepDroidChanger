namespace ScrcpyNet.Wpf;

internal static class ScrcpyDisplayTouchPolicy
{
    public static AndroidMotionEventButtons GetButtons(
        AndroidMotionEventAction action,
        ulong pointerId)
    {
        if (pointerId == ScrcpyDisplay.VirtualPointerId || IsRelease(action))
            return (AndroidMotionEventButtons)0;

        return AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY;
    }

    public static float GetPressure(AndroidMotionEventAction action)
    {
        return IsRelease(action) ? 0f : 1f;
    }

    private static bool IsRelease(AndroidMotionEventAction action)
    {
        return action is AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP or
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_CANCEL;
    }
}
