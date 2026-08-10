using DeepDroidChanger.Services;
using System.Globalization;

namespace DeepDroidChanger.Helpers;

internal static class LocationCoordinateRandomizer
{
    private const int RandomDecimalCeiling = 1000;
    private const double CoordinateBlockScale = 10d;
    private const double RandomCoordinateScale = 10000d;
    private const double MinLatitude = -90d;
    private const double MaxLatitude = 90d;
    private const double MinLongitude = -180d;
    private const double MaxLongitude = 180d;

    public static string RandomizeLatitude(double coordinate, IRandomService randomService)
    {
        return RandomizeCoordinate(coordinate, MinLatitude, MaxLatitude, randomService);
    }

    public static string RandomizeLongitude(double coordinate, IRandomService randomService)
    {
        return RandomizeCoordinate(coordinate, MinLongitude, MaxLongitude, randomService);
    }

    private static string RandomizeCoordinate(
        double coordinate,
        double minimum,
        double maximum,
        IRandomService randomService)
    {
        ArgumentNullException.ThrowIfNull(randomService);

        var blockStart = coordinate >= 0d
            ? Math.Floor(coordinate * CoordinateBlockScale) / CoordinateBlockScale
            : Math.Ceiling(coordinate * CoordinateBlockScale) / CoordinateBlockScale;
        var randomOffset = randomService.RandomInRange(0, RandomDecimalCeiling) / RandomCoordinateScale;
        var randomized = coordinate >= 0d
            ? blockStart + randomOffset
            : blockStart - randomOffset;

        return Math.Clamp(randomized, minimum, maximum).ToString("F4", CultureInfo.InvariantCulture);
    }
}
