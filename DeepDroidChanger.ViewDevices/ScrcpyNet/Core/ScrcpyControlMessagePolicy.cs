namespace ScrcpyNet;

internal static class ScrcpyControlMessagePolicy
{
    public static bool IsDroppable(IControlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message switch
        {
            TouchEventControlMessage touch =>
                touch.Action == AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
            ScrollEventControlMessage => true,
            _ => false
        };
    }
}
