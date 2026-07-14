namespace DeepDroidChanger.Models
{
    public sealed class IpGeolocationInfo
    {
        public bool Success { get; set; }
        public string PublicIp { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Timezone { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
