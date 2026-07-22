namespace DeepDroidChanger.Models;

public sealed class DeviceProfileConfig
{
    public string Brand { get; set; } = string.Empty;
    public string AndroidVersion { get; set; } = string.Empty;
    public bool ChangeSimEnabled { get; set; } = true;
    public bool UseIntegritySecurityPatch { get; set; } = true;
    public string CountryIso { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
    public string CarrierMcc { get; set; } = string.Empty;
    public string CarrierMnc { get; set; } = string.Empty;
    public DeviceChangeOptions ChangeOptions { get; set; } = new();
}
