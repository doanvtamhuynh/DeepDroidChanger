using System.Globalization;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.Helpers;

internal static class DeviceProfileNormalizer
{
    private const string BuildDateFormat = "ddd MMM dd HH:mm:ss 'UTC' yyyy";

    public static bool IsMissing(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || value.Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("n/a", StringComparison.OrdinalIgnoreCase);
    }

    public static string First(params string?[] values)
        => values.FirstOrDefault(value => !IsMissing(value))?.Trim() ?? string.Empty;

    public static void NormalizeIdentity(DeviceInfoApiDevice device)
    {
        FillFromFingerprint(device);
        ApplyIdentityFallbacks(device);
        ValidateIdentity(device);
    }

    private static void FillFromFingerprint(DeviceInfoApiDevice device)
    {
        string[] parts = device.Fingerprint?.Trim().Split('/') ?? [];
        if (parts.Length != 6)
            throw new DeviceRandomApiException("Random device fingerprint is invalid.");

        string[] deviceRelease = parts[2].Split(':');
        string[] incrementalType = parts[4].Split(':');
        if (deviceRelease.Length != 2 || incrementalType.Length != 2
            || parts.Any(IsMissing)
            || deviceRelease.Any(IsMissing)
            || incrementalType.Any(IsMissing))
        {
            throw new DeviceRandomApiException("Random device fingerprint is incomplete.");
        }

        device.Brand = First(device.Brand, parts[0]);
        device.Product = First(device.Product, parts[1]);
        device.Code = First(device.Code, deviceRelease[0]);
        device.Release = First(device.Release, deviceRelease[1]);
        device.BuildId = First(device.BuildId, parts[3]);
        device.BuildIncremental = First(device.BuildIncremental, incrementalType[0]);
        device.Type = First(device.Type, incrementalType[1]);
        device.Tags = First(device.Tags, parts[5]);
        device.Sdk = First(device.Sdk, InferSdk(device.Release));
    }

    private static void ApplyIdentityFallbacks(DeviceInfoApiDevice device)
    {
        device.Manufacturer = First(device.Manufacturer, device.Brand);
        device.Name = First(device.Name, device.Product);
        device.Board = First(device.Board, device.Code);
        device.Hardware = First(device.Hardware, device.Code);
        device.Platform = First(device.Platform, device.Board);
        device.SocManufacturer = First(device.SocManufacturer,
            char.ToUpperInvariant(device.Brand[0]) + device.Brand[1..]);
        device.SocModel = First(device.SocModel, device.Platform);
        device.BuildDisplayId = First(device.BuildDisplayId, $"{device.BuildId}.{device.BuildIncremental}");
        device.BuildFlavor = First(device.BuildFlavor, $"{device.Product}-user");
        device.BuildDescription = First(device.BuildDescription,
            $"{device.Product}-user {device.Release} {device.BuildId} {device.BuildIncremental} {device.Tags}");
        device.Bootloader = First(device.Bootloader, device.BuildIncremental);
        device.Baseband = First(device.Baseband, device.BuildIncremental);
    }

    private static void ValidateIdentity(DeviceInfoApiDevice device)
    {
        string?[] required = [device.Model, device.Brand, device.Manufacturer, device.Product,
            device.Code, device.Name, device.Release, device.Sdk, device.BuildId, device.BuildIncremental,
            device.Board, device.Hardware, device.Platform, device.SocManufacturer, device.SocModel,
            device.BuildDisplayId, device.BuildFlavor, device.BuildDescription, device.Type, device.Tags,
            device.Bootloader, device.Baseband, device.SecurityPatch];
        if (required.Any(IsMissing))
            throw new DeviceRandomApiException("Random device identity is incomplete.");
        if (!DateTime.TryParseExact(device.SecurityPatch, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            throw new DeviceRandomApiException("Random device security patch is invalid.");
    }

    public static void NormalizeBuildDates(DeviceInfoApiDevice device, IRandomService random)
    {
        bool hasUtc = TryParseBuildDateUtc(device.BuildDateUtc, out DateTimeOffset fromUtc);
        bool hasDate = TryParseBuildDate(device.BuildDate, device.Brand, out DateTimeOffset fromDate);
        if (hasUtc && hasDate)
            return;

        DateTimeOffset buildDateUtc = hasUtc ? fromUtc : hasDate ? fromDate
            : GenerateFallbackBuildDateUtc(device.SecurityPatch, random);
        device.BuildDateUtc = buildDateUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        if (!hasDate || hasUtc)
            device.BuildDate = buildDateUtc.ToString(BuildDateFormat, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset GenerateFallbackBuildDateUtc(string? securityPatch, IRandomService random)
    {
        if (!DateTime.TryParseExact(securityPatch?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime patch))
        {
            throw new DeviceRandomApiException("Cannot generate build date without a valid security patch.");
        }

        DateTimeOffset start = new DateTimeOffset(patch, TimeSpan.Zero).AddDays(3);
        DateTimeOffset endExclusive = new DateTimeOffset(patch, TimeSpan.Zero).AddDays(31);
        return start.AddSeconds(random.RandomInRange(0L, (long)(endExclusive - start).TotalSeconds));
    }

    private static bool TryParseBuildDate(string? value, string brand, out DateTimeOffset date)
    {
        date = default;
        if (IsMissing(value))
            return false;

        string normalized = string.Join(" ", value!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string[] parts = normalized.Split(' ');
        if (parts.Length == 6)
        {
            double? offsetHours = parts[4] switch
            {
                "UTC" or "GMT" => 0, "KST" => 9, "HKT" => 8, "WIB" or "+07" => 7,
                "CDT" or "EST" => -5, "EDT" => -4, "PDT" => -7,
                "CST" => brand.Equals("motorola", StringComparison.OrdinalIgnoreCase) ? -6 : 8,
                _ => null
            };
            if (offsetHours.HasValue)
            {
                string dateText = string.Join(" ", parts.Take(4).Append(parts[5]));
                if (DateTime.TryParseExact(dateText, ["ddd MMM d HH:mm:ss yyyy", "ddd MMM dd HH:mm:ss yyyy"],
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                {
                    date = new DateTimeOffset(parsed, TimeSpan.FromHours(offsetHours.Value)).ToUniversalTime();
                    return true;
                }
                return false;
            }
        }

        // Only accept explicit numeric offsets or Z here; never infer the Windows timezone.
        bool explicitZone = normalized.EndsWith('Z')
            || System.Text.RegularExpressions.Regex.IsMatch(normalized, @"[+-]\d{2}:?\d{2}$");
        return explicitZone && DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
    }

    private static bool TryParseBuildDateUtc(string? value, out DateTimeOffset buildDateUtc)
    {
        buildDateUtc = default;
        if (!long.TryParse(
                value?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long unixSeconds))
        {
            return false;
        }

        try
        {
            buildDateUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string InferSdk(string release) => release switch
    {
        "9" => "28", "10" => "29", "11" => "30", "12" => "31", "13" => "33",
        "14" => "34", "15" => "35", "16" => "36", "17" => "37", _ => string.Empty
    };
}
