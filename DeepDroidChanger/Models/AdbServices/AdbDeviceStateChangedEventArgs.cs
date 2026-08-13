namespace DeepDroidChanger.Models;

public sealed class AdbDeviceStateChangedEventArgs : EventArgs
{
    public AdbDeviceStateChangedEventArgs(string serial, AdbDevice? previous, AdbDevice? current)
    {
        Serial = serial;
        Previous = previous;
        Current = current;
    }

    public string Serial { get; }

    public AdbDevice? Previous { get; }

    public AdbDevice? Current { get; }
}
