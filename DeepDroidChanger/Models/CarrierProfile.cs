namespace DeepDroidChanger.Models
{
    public sealed class CarrierProfile
    {
        public CarrierProfile(string countryIso, string countryCode, string countryName, string carrierName, string mcc, string mnc)
        {
            CountryIso = countryIso;
            CountryCode = countryCode;
            CountryName = countryName;
            CarrierName = carrierName;
            Mcc = mcc;
            Mnc = mnc;
        }

        public string CountryIso { get; }
        public string CountryCode { get; }
        public string CountryName { get; }
        public string CarrierName { get; }
        public string Mcc { get; }
        public string Mnc { get; }
    }
}
