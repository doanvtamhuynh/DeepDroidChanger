namespace ScrcpyNet;

internal static class FfmpegSendDrainFlow
{
    public static void SendPacketAndDrain(
        Func<int> sendPacket,
        Action drainFrames,
        Func<int, bool> isAgain,
        Func<int, Exception> createFatalException)
    {
        ArgumentNullException.ThrowIfNull(sendPacket);
        ArgumentNullException.ThrowIfNull(drainFrames);
        ArgumentNullException.ThrowIfNull(isAgain);
        ArgumentNullException.ThrowIfNull(createFatalException);

        while (true)
        {
            int result = sendPacket();
            if (isAgain(result))
            {
                drainFrames();
                continue;
            }

            if (result < 0)
                throw createFatalException(result);

            drainFrames();
            return;
        }
    }
}
