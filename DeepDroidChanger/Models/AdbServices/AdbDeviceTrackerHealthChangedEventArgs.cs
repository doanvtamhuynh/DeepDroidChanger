namespace DeepDroidChanger.Models;

public sealed class AdbDeviceTrackerHealthChangedEventArgs : EventArgs
{
    public AdbDeviceTrackerHealthChangedEventArgs(
        AdbDeviceTrackerHealth previous,
        AdbDeviceTrackerHealth current)
    {
        Previous = previous;
        Current = current;
    }

    public AdbDeviceTrackerHealth Previous { get; }

    public AdbDeviceTrackerHealth Current { get; }
}
