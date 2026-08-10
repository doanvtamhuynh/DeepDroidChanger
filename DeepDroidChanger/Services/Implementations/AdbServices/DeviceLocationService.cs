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
                        IReadOnlyList<LocationOption> countryLocations = string.IsNullOrWhiteSpace(geoInfo.CountryCode)
                            ? Array.Empty<LocationOption>()
                            : locations
                                .Where(loc => string.Equals(
                                    loc.CountryCode,
                                    geoInfo.CountryCode,
                                    StringComparison.OrdinalIgnoreCase))
                                .ToArray();
                        IReadOnlyList<LocationOption> candidates = countryLocations.Count > 0
                            ? countryLocations
                            : locations;
                        LocationOption? match = candidates
                            .OrderBy(loc => CalculateGreatCircleDistance(
                                geoInfo.Latitude,
                                geoInfo.Longitude,
                                loc.Latitude,
                                loc.Longitude))
                            .FirstOrDefault();

                        if (match != null)
                        {
                            countryCode = match.CountryCode;
                            cityName = match.CityName;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Failed to resolve city name for IP geolocation.");
                }
            }

            return new DeviceLocationResult(randomizedLat, randomizedLon, countryCode, cityName);
        }

        private static double CalculateGreatCircleDistance(
            double latitude1,
            double longitude1,
            double latitude2,
            double longitude2)
        {
            const double DegreesToRadians = Math.PI / 180d;

            double latitudeDelta = (latitude2 - latitude1) * DegreesToRadians;
            double longitudeDelta = (longitude2 - longitude1) * DegreesToRadians;
            double latitude1Radians = latitude1 * DegreesToRadians;
            double latitude2Radians = latitude2 * DegreesToRadians;

            double sinLatitudeDelta = Math.Sin(latitudeDelta / 2d);
            double sinLongitudeDelta = Math.Sin(longitudeDelta / 2d);
            double haversine = sinLatitudeDelta * sinLatitudeDelta
                + Math.Cos(latitude1Radians)
                    * Math.Cos(latitude2Radians)
                    * sinLongitudeDelta
                    * sinLongitudeDelta;

            double clampedHaversine = Math.Clamp(haversine, 0d, 1d);
            return 2d * Math.Atan2(
                Math.Sqrt(clampedHaversine),
                Math.Sqrt(1d - clampedHaversine));
        }

        public async Task<DeviceLocationResult> ApplyCatalogLocationAsync(
            string serial,
            LocationOption location,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(location);

            string latitude = LocationCoordinateRandomizer.RandomizeLatitude(location.Latitude, _randomService);
            string longitude = LocationCoordinateRandomizer.RandomizeLongitude(location.Longitude, _randomService);
            await ApplyLocationAsync(serial, latitude, longitude, cancellationToken).ConfigureAwait(false);

            return new DeviceLocationResult(
                latitude,
                longitude,
                location.CountryCode,
                location.CityName);
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
