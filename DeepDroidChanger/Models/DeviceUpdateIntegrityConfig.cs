namespace DeepDroidChanger.Models;

public sealed class DeviceUpdateIntegrityConfig
{
    public bool FromServer { get; set; } = true;
    public string IntegrityFile { get; set; } = string.Empty;
    public string KeyboxFile { get; set; } = string.Empty;
    public bool IntegrityEnabled { get; set; } = true;
    public bool KeyboxEnabled { get; set; } = true;
}
