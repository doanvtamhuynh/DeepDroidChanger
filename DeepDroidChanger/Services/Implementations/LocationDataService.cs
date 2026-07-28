using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class LocationDataService : ILocationDataService
    {
        private const string CountryCodePropertyName = "country_code";
        private const string CountryNamePropertyName = "country_name";
        private const string CityNamePropertyName = "city_name";
        private const string TimezonePropertyName = "timezone";
        private const string GmtOffsetPropertyName = "gmt_offset";
        private const string LatitudePropertyName = "latitude";
        private const string LongitudePropertyName = "longitude";

        private readonly ILogger<LocationDataService> _logger;
        private IReadOnlyList<LocationOption>? _locations;

        public LocationDataService(ILogger<LocationDataService> logger)
        {
            _logger = logger;
        }

        public Task<IReadOnlyList<LocationOption>> GetLocationsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _locations ??= LoadLocations(cancellationToken);
            return Task.FromResult(_locations);
        }

        private IReadOnlyList<LocationOption> LoadLocations(CancellationToken cancellationToken)
        {
            try
            {
                var json = AssetDataReader.ReadText(AssetConstants.Data.LocationTimezonesPath);
                using var document = JsonDocument.Parse(json);
                var options = new List<LocationOption>();

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var countryCode = GetStringValue(element, CountryCodePropertyName);
                    var countryName = GetStringValue(element, CountryNamePropertyName);
                    var cityName = GetStringValue(element, CityNamePropertyName);
                    var timezone = GetStringValue(element, TimezonePropertyName);
                    var gmtOffset = GetStringValue(element, GmtOffsetPropertyName);
                    var latitude = GetDoubleValue(element, LatitudePropertyName);
                    var longitude = GetDoubleValue(element, LongitudePropertyName);

                    if (countryCode.Length == 0 || countryName.Length == 0)
                        continue;

                    options.Add(new LocationOption(
                        countryCode,
                        countryName,
                        cityName,
                        timezone,
                        gmtOffset,
                        latitude,
                        longitude));
                }

                return options
                    .OrderBy(option => option.CountryName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(option => option.CityName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(option => option.Timezone, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to load location data.");
                return Array.Empty<LocationOption>();
            }
        }

        private static string GetStringValue(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
                return string.Empty;

            return property.GetString()?.Trim() ?? string.Empty;
        }

        private static double GetDoubleValue(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
                return 0d;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var val))
                return val;

            return 0d;
        }
    }
}
