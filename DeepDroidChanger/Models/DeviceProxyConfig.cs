namespace DeepDroidChanger.Models;

public sealed class DeviceProxyConfig
{
    public string FullString { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool ChangeLocationByIp { get; set; } = true;
    public bool ChangeTimezoneByIp { get; set; } = true;
}
