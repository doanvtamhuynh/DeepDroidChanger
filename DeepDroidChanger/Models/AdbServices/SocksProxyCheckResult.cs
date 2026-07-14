namespace DeepDroidChanger.Models
{
    public sealed class SocksProxyCheckResult
    {
        public SocksProxyCheckResult(string publicIp, string countryCode)
        {
            PublicIp = publicIp;
            CountryCode = countryCode;
        }

        public string PublicIp { get; }
        public string CountryCode { get; }
    }
}
