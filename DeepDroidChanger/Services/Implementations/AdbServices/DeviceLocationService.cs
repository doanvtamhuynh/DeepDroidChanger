using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceLocationService : IDeviceLocationService
    {
        private const int RandomDecimalCeiling = 1000;
        private const double CoordinateBlockScale = 10d;
        private const double RandomCoordinateScale = 10000d;
        private const double MinLatitude = -90d;
        private const double MaxLatitude = 90d;
        private const double MinLongitude = -180d;
        private const double MaxLongitude = 180d;

        private readonly IAdbCommandService _adbCommandService;
        private readonly IIpGeolocationService _adbIpGeolocationService;
        private readonly IRandomService _randomService;
        private readonly ILogger<DeviceLocationService> _logger;

        public DeviceLocationService(
            IAdbCommandService adbCommandService,
            IIpGeolocationService adbIpGeolocationService,
            IRandomService randomService,
            ILogger<DeviceLocationService> logger)
        {
            _adbCommandService = adbCommandService;
            _adbIpGeolocationService = adbIpGeolocationService;
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

            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Prop_Latitude, safeLat, cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Prop_Longitude, safeLon, cancellationToken).ConfigureAwait(false);
        }

        public async Task<(string Latitude, string Longitude)> ResolveLocationByDeviceIpAsync(string serial, CancellationToken cancellationToken)
        {
            var geoInfo = await _adbIpGeolocationService.GetDeviceIpGeolocationAsync(serial, cancellationToken).ConfigureAwait(false);

            if (!IsValidResolvedCoordinate(geoInfo.Latitude, geoInfo.Longitude, geoInfo.CountryCode))
                throw new InvalidOperationException("Failed to resolve location by device IP.");

            var randomizedLat = RandomizeCoordinate(geoInfo.Latitude, MinLatitude, MaxLatitude);
            var randomizedLon = RandomizeCoordinate(geoInfo.Longitude, MinLongitude, MaxLongitude);

            return (randomizedLat, randomizedLon);
        }

        public async Task<(string Latitude, string Longitude)> ApplyAsync(
            string serial,
            ChangeLocationDialogResult result,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (result.Mode == ChangeLocationMode.DeviceIp)
            {
                (string latitude, string longitude) =
                    await ResolveLocationByDeviceIpAsync(serial, cancellationToken).ConfigureAwait(false);
                await ApplyLocationAsync(serial, latitude, longitude, cancellationToken).ConfigureAwait(false);
                return (latitude, longitude);
            }

            await ApplyLocationAsync(
                    serial,
                    result.Latitude,
                    result.Longitude,
                    cancellationToken)
                .ConfigureAwait(false);
            return (
                double.Parse(result.Latitude, CultureInfo.InvariantCulture).ToString("F4", CultureInfo.InvariantCulture),
                double.Parse(result.Longitude, CultureInfo.InvariantCulture).ToString("F4", CultureInfo.InvariantCulture));
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

        private string RandomizeCoordinate(double coordinate, double minimum, double maximum)
        {
            var blockStart = coordinate >= 0d
                ? Math.Floor(coordinate * CoordinateBlockScale) / CoordinateBlockScale
                : Math.Ceiling(coordinate * CoordinateBlockScale) / CoordinateBlockScale;
            var randomOffset = _randomService.RandomInRange(0, RandomDecimalCeiling) / RandomCoordinateScale;
            var randomized = coordinate >= 0d
                ? blockStart + randomOffset
                : blockStart - randomOffset;

            return Math.Clamp(randomized, minimum, maximum).ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}
