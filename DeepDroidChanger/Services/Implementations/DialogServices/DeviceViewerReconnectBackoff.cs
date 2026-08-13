namespace DeepDroidChanger.Services;

/// <summary>
/// Keeps reconnect failures across short-lived scrcpy sessions.
/// </summary>
internal sealed class DeviceViewerReconnectBackoff
{
    internal static readonly TimeSpan StabilityInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30)
    ];

    private int _failureCount;

    internal int FailureCount => Volatile.Read(ref _failureCount);

    internal TimeSpan RegisterFailure()
    {
        var failureCount = Interlocked.Increment(ref _failureCount);
        return GetDelay(failureCount);
    }

    internal TimeSpan GetCurrentDelay()
    {
        return GetDelay(FailureCount);
    }

    internal void Reset()
    {
        Volatile.Write(ref _failureCount, 0);
    }

    internal static TimeSpan GetDelay(int failureCount)
    {
        if (failureCount <= 0)
            return TimeSpan.Zero;

        return Delays[Math.Min(failureCount - 1, Delays.Length - 1)];
    }
}
