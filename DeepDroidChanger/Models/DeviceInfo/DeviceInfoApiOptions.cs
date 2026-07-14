namespace DeepDroidChanger.Models
{
    public sealed class DeviceInfoApiOptions
    {
        public string Endpoint { get; set; } = string.Empty;
        public string UserPoolId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string Region { get; set; } = "ap-southeast-1";
        public string AuthenticationHeaderName { get; set; } = "authorization";
    }
}
