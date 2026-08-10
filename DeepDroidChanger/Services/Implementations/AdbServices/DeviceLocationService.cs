using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceLocationService : IDeviceLocationService
    {
        private const double MinLatitude = -90d;
        private const double MaxLatitude = 90d;
        private const double MinLongitude = -180d;
        private const double MaxLongitude = 180d;

        private readonly IAdbCommandService _adbCommandService;
        private readonly IIpGeolocationService _adbIpGeolocationService;
        private readonly ILocationDataService? _locationDataService;
        private readonly IRandomService _randomService;
        private readonly ILogger<DeviceLocationService> _logger;

        public DeviceLocationService(
            IAdbCommandService adbCommandService,
            IIpGeolocationService adbIpGeolocationService,
            IRandomService randomService,
            ILogger<DeviceLocationService> logger)
            : this(adbCommandService, adbIpGeolocationService, null, randomService, logger)
        {
        }

        public DeviceLocationService(
            IAdbCommandService adbCommandService,
            IIpGeolocationService adbIpGeolocationService,
            ILocationDataService? locationDataService,
            IRandomService randomService,
            ILogger<DeviceLocationService> logger)
        {
            _adbCommandService = adbCommandService;
            _adbIpGeolocationService = adbIpGeolocationService;
            _locationDataService = locationDataService;
            _randomService = randomService;
            _logger = logger;
        }

        public async Task ApplyLocationAsync(string serial, string latitude, string longitude, CancellationToken cancellationToken)
        {
            if (!TryParseLatitude(latitude, out var lat))
                throw new ArgumentException("Invalid latitude format or range.", nameof(latitude));

            if (!TryParseLongitude(longitude, out var lon))
                throw new ArgumentException("Invalid longitude format or range.", nameof(longitude));

            var safeLat = lat.ToString("F4", CultureInfo.InvariantCulture);
            var safeLon = lon.ToString("F4", CultureInfo.InvariantCulture);

            _logger.LogInformation("Applying configured location to device {Serial}.", serial);

            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Latitude, safeLat, cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Longitude, safeLon, cancellationToken).ConfigureAwait(false);
        }

        public async Task<DeviceLocationResult> ResolveLocationByDeviceIpAsync(string serial, CancellationToken cancellationToken)
        {
            var geoInfo = await _adbIpGeolocationService.GetDeviceIpGeolocationAsync(serial, cancellationToken).ConfigureAwait(false);

            if (!IsValidResolvedCoordinate(geoInfo.Latitude, geoInfo.Longitude, geoInfo.CountryCode))
                throw new InvalidOperationException("Failed to resolve location by device IP.");

            var randomizedLat = LocationCoordinateRandomizer.RandomizeLatitude(geoInfo.Latitude, _randomService);
            var randomizedLon = LocationCoordinateRandomizer.RandomizeLongitude(geoInfo.Longitude, _randomService);

            string countryCode = geoInfo.CountryCode;
            string cityName = string.Empty;

            if (_locationDataService != null)
            {
                try
                {
                    var locations = await _locationDataService.GetLocationsAsync(cancellationToken).ConfigureAwait(false);
                    if (locations.Count > 0)
                    {
                        LocationOption? match = null;
                        if (!string.IsNullOrWhiteSpace(geoInfo.CountryCode))
                        {
                            match = locations.FirstOrDefault(loc =>
                                string.Equals(loc.CountryCode, geoInfo.CountryCode, StringComparison.OrdinalIgnoreCase));
                        }

                        if (match == null)
                        {
                            match = locations
                                .OrderBy(loc => Math.Pow(loc.Latitude - geoInfo.Latitude, 2) + Math.Pow(loc.Longitude - geoInfo.Longitude, 2))
                                .FirstOrDefault();
                        }

                        if (match != null)
                        {
                            countryCode = match.CountryCode;
                            cityName = match.CityName;
                        }
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Failed to resolve city name for IP geolocation.");
                }
            }

            return new DeviceLocationResult(randomizedLat, randomizedLon, countryCode, cityName);
        }

        public async Task<DeviceLocationResult> ApplyAsync(
            string serial,
            ChangeLocationDialogResult result,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (result.Mode == ChangeLocationMode.DeviceIp)
            {
                DeviceLocationResult locationResult =
                    await ResolveLocationByDeviceIpAsync(serial, cancellationToken).ConfigureAwait(false);
                await ApplyLocationAsync(serial, locationResult.Latitude, locationResult.Longitude, cancellationToken).ConfigureAwait(false);
                return locationResult;
            }

            await ApplyLocationAsync(
                    serial,
                    result.Latitude,
                    result.Longitude,
                    cancellationToken)
                .ConfigureAwait(false);

            string safeLat = double.Parse(result.Latitude, CultureInfo.InvariantCulture).ToString("F4", CultureInfo.InvariantCulture);
            string safeLon = double.Parse(result.Longitude, CultureInfo.InvariantCulture).ToString("F4", CultureInfo.InvariantCulture);

            return new DeviceLocationResult(safeLat, safeLon);
        }

        private static bool IsValidResolvedCoordinate(double latitude, double longitude, string countryCode)
        {
            if (!IsLatitudeInRange(latitude) || !IsLongitudeInRange(longitude))
                return false;

            return latitude != 0d
                || longitude != 0d
                || !string.IsNullOrWhiteSpace(countryCode);
        }

        private static bool TryParseLatitude(string value, out double latitude)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
                && IsLatitudeInRange(latitude);
        }

        private static bool TryParseLongitude(string value, out double longitude)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)
                && IsLongitudeInRange(longitude);
        }

        private static bool IsLatitudeInRange(double latitude)
        {
            return latitude is >= MinLatitude and <= MaxLatitude;
        }

        private static bool IsLongitudeInRange(double longitude)
        {
            return longitude is >= MinLongitude and <= MaxLongitude;
        }

    }
}
