namespace DeepDroidChanger.Models
{
    public sealed class CountryOption
    {
        public CountryOption(string countryCode, string countryName)
        {
            CountryCode = countryCode;
            CountryName = countryName;
            CountryDisplayText = $"{CountryName} ({CountryCode})";
        }

        public string CountryCode { get; }
        public string CountryName { get; }
        public string CountryDisplayText { get; }
    }
}
