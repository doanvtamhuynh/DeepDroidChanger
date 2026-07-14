namespace DeepDroidChanger.Models
{
    public sealed class TimezoneOption
    {
        public TimezoneOption(
            string countryCode,
            string countryName,
            string timezone,
            string gmtOffset,
            string displayText)
        {
            CountryCode = countryCode;
            CountryName = countryName;
            Timezone = timezone;
            GmtOffset = gmtOffset;
            DisplayText = displayText;
        }

        public string CountryCode { get; }
        public string CountryName { get; }
        public string Timezone { get; }
        public string GmtOffset { get; }

        public string DisplayText { get; }
    }
}
