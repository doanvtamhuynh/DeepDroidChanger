using System.Globalization;

namespace DeepDroidChanger.Models
{
    public sealed class LocationOption
    {
        public LocationOption(
            string countryCode,
            string countryName,
            string cityName,
            string timezone,
            string gmtOffset,
            double latitude,
            double longitude)
        {
            CountryCode = countryCode;
            CountryName = countryName;
            CityName = cityName;
            Timezone = timezone;
            GmtOffset = gmtOffset;
            Latitude = latitude;
            Longitude = longitude;
            LatitudeString = latitude.ToString("F4", CultureInfo.InvariantCulture);
            LongitudeString = longitude.ToString("F4", CultureInfo.InvariantCulture);

            CountryDisplayText = string.IsNullOrEmpty(countryCode)
                ? countryName
                : $"{countryName} ({countryCode})";

            var locationName = string.IsNullOrEmpty(cityName) ? countryName : cityName;
            LocationDisplayText = $"{locationName} [{LatitudeString}, {LongitudeString}]";
        }

        public string CountryCode { get; }
        public string CountryName { get; }
        public string CityName { get; }
        public string Timezone { get; }
        public string GmtOffset { get; }
        public double Latitude { get; }
        public double Longitude { get; }
        public string LatitudeString { get; }
        public string LongitudeString { get; }

        public string CountryDisplayText { get; }
        public string LocationDisplayText { get; }
    }
}
