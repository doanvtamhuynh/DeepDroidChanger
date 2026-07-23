namespace DeepDroidChanger.Models
{
    public sealed class TimezoneOption
    {
        public TimezoneOption(
            string countryCode,
            string countryName,
            string timezone,
            string gmtOffset)
        {
            CountryCode = countryCode;
            CountryName = countryName;
            Timezone = timezone;
            GmtOffset = gmtOffset;
            TimezoneDisplayText = string.IsNullOrEmpty(gmtOffset)
                ? timezone
                : $"{timezone} ({gmtOffset})";
        }

        public string CountryCode { get; }
        public string CountryName { get; }
        public string Timezone { get; }
        public string GmtOffset { get; }

        public string TimezoneDisplayText { get; }
    }
}
