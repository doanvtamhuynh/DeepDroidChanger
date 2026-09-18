namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetDeviceBusyException : InvalidOperationException
{
    public ScrcpyNetDeviceBusyException(string serial)
        : base($"Device '{serial}' is already open in another viewer.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        Serial = serial;
    }

    public string Serial { get; }
}
