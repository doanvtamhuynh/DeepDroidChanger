namespace DeepDroidChanger.Models
{
    public sealed class RandomDeviceRequest
    {
        public string? SelectedBrand { get; set; }
        public string? SelectedAndroidVersion { get; set; }
        public bool UseIntegritySecurityPatch { get; set; }
        public CarrierCountryOption? Country { get; set; }
        public CarrierOption? Carrier { get; set; }
    }
}
