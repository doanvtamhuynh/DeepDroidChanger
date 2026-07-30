namespace DeepDroidChanger.Models;

public sealed class MultipleDeviceChangeConfig
{
    public string Brand { get; set; } = "Random";
    public string AndroidVersion { get; set; } = "Random";
    public string Model { get; set; } = string.Empty;
    public string CountryIso { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
    public string CarrierMcc { get; set; } = string.Empty;
    public string CarrierMnc { get; set; } = string.Empty;
    public bool ChangeSimEnabled { get; set; } = true;
    public bool UseIntegritySecurityPatch { get; set; } = true;
}
