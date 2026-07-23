namespace DeepDroidChanger.Models
{
    public sealed class StoredDeviceConfig
    {
        public string Serial { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string CountryIso { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public string Carrier { get; set; } = string.Empty;
        public string CarrierMcc { get; set; } = string.Empty;
        public string CarrierMnc { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string AndroidVersion { get; set; } = string.Empty;
        public bool ChangeSimEnabled { get; set; } = true;
        public bool UseIntegritySecurityPatch { get; set; } = true;
        public DeviceChangeOptions ChangeOptions { get; set; } = new();
        public bool UpdateIntegrityFromServer { get; set; } = true;
        public string UpdateIntegrityFile { get; set; } = string.Empty;
        public string UpdateKeyboxFile { get; set; } = string.Empty;
        public bool UpdateIntegrityEnabled { get; set; } = true;
        public bool UpdateKeyboxEnabled { get; set; } = true;
        public string LocationMode { get; set; } = string.Empty;
        public string LocationLatitude { get; set; } = string.Empty;
        public string LocationLongitude { get; set; } = string.Empty;
        public string LocationCountryCode { get; set; } = string.Empty;
        public string LocationCityName { get; set; } = string.Empty;
        public string TimezoneMode { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public string ProxyFullString { get; set; } = string.Empty;
        public string ProxyType { get; set; } = string.Empty;
        public bool ProxyChangeLocationByIp { get; set; } = true;
        public bool ProxyChangeTimezoneByIp { get; set; } = true;
    }
}
