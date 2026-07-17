namespace DeepDroidChanger.Models;

public sealed class DeviceChangeOptions
{
    public bool UseDefaultMode { get; set; } = true;
    public bool ChangeAndroidId { get; set; }
    public bool ChangeMacAddress { get; set; } = true;
    public bool UseRmRfForPackageCleanup { get; set; }
    public bool ClearAllPackages { get; set; } = true;
    public bool ClearSelectedPackages { get; set; }
    public bool ClearGooglePackages { get; set; }
    public bool ClearGoogleAccounts { get; set; } = true;
    public List<string> SelectedPackages { get; set; } = [];
}
