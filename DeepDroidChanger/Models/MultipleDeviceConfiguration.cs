namespace DeepDroidChanger.Models;

public sealed class MultipleDeviceConfiguration
{
    public MultipleDeviceChangeConfig ChangeConfig { get; set; } = new();
    public DeviceChangeOptions ChangeOptions { get; set; } = new();
}
