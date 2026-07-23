namespace DeepDroidChanger.Models
{
    public sealed class DeviceLocationResult
    {
        public DeviceLocationResult(
            string latitude,
            string longitude,
            string countryCode = "",
            string cityName = "")
        {
            Latitude = latitude ?? string.Empty;
            Longitude = longitude ?? string.Empty;
            CountryCode = countryCode ?? string.Empty;
            CityName = cityName ?? string.Empty;
        }

        public string Latitude { get; }
        public string Longitude { get; }
        public string CountryCode { get; }
        public string CityName { get; }

        public void Deconstruct(out string latitude, out string longitude)
        {
            latitude = Latitude;
            longitude = Longitude;
        }

        public void Deconstruct(out string latitude, out string longitude, out string countryCode, out string cityName)
        {
            latitude = Latitude;
            longitude = Longitude;
            countryCode = CountryCode;
            cityName = CityName;
        }
    }
}
