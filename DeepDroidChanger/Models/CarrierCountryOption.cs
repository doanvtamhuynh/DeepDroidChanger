namespace DeepDroidChanger.Models
{
    public sealed class CarrierCountryOption
    {
        public CarrierCountryOption(string countryIso, string countryCode, string countryName)
        {
            CountryIso = countryIso;
            CountryCode = countryCode;
            CountryName = countryName;
            DisplayName = $"{countryName} ({countryIso.ToUpperInvariant()})";
        }

        public string CountryIso { get; }
        public string CountryCode { get; }
        public string CountryName { get; }
        public string DisplayName { get; }
    }
}
