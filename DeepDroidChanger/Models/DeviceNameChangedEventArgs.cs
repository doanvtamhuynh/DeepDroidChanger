namespace DeepDroidChanger.Models;

public sealed class DeviceNameChangedEventArgs : EventArgs
{
    public DeviceNameChangedEventArgs(string serial, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Serial = serial.Trim();
        Name = name.Trim();
    }

    public string Serial { get; }

    public string Name { get; }
}
