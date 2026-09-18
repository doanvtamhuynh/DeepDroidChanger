namespace ScrcpyNet;

internal static class FfmpegSendDrainFlow
{
    public static void SendPacketAndDrain(
        Func<int> sendPacket,
        Func<int> drainFrames,
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
                int drainedFrames = drainFrames();
                if (drainedFrames == 0)
                {
                    throw new InvalidOperationException(
                        "FFmpeg returned EAGAIN while sending a packet, but draining produced no frames.");
                }

                continue;
            }

            if (result < 0)
                throw createFatalException(result);

            drainFrames();
            return;
        }
    }
}
