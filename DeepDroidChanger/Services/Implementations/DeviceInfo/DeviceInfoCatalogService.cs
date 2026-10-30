using System.Globalization;
using System.Text.Json;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceInfoCatalogService : IDeviceInfoCatalogService, IDeviceRandomApiService
{
    private readonly IRandomService _random;
    private readonly ILogger<DeviceInfoCatalogService> _logger;
    private readonly Lazy<CatalogSnapshot> _snapshot;

    public DeviceInfoCatalogService(IRandomService random, ILogger<DeviceInfoCatalogService> logger)
    {
        _random = random;
        _logger = logger;
        _snapshot = new Lazy<CatalogSnapshot>(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => { _ = _snapshot.Value; }, cancellationToken);
    }

    private CatalogSnapshot Snapshot => _snapshot.IsValueCreated
        ? _snapshot.Value
        : throw new DeviceRandomApiException("Device info catalog has not been initialized.");

    public IReadOnlyList<string> Brands => Snapshot.Brands;

    public IReadOnlyList<string> GetAndroidVersions(string? brand)
    {
        CatalogSnapshot snapshot = Snapshot;
        return snapshot.AndroidVersions.TryGetValue(IsRandom(brand) ? "Random" : brand!.Trim(), out var versions)
            ? versions
            : [];
    }

    public Task<DeviceInfoApiDevice> GetRandomDeviceAsync(
        RandomDeviceSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        var profilesByBrand = Snapshot.Profiles;
        IEnumerable<IReadOnlyDictionary<string, DeviceInfoApiDevice[]>> brands = IsRandom(selection.Brand)
            ? profilesByBrand.Values
            : profilesByBrand.TryGetValue(selection.Brand.Trim(), out var matchingBrand) ? [matchingBrand] : [];
        var eligibleBrands = brands.Select(profiles => GetEligibleReleases(profiles, selection))
            .Where(releases => releases.Length > 0).ToArray();

        if (eligibleBrands.Length == 0)
            throw new DeviceRandomApiException("No local device matches the selected brand and Android version.");

        var brand = _random.PickRandom(eligibleBrands);
        var release = _random.PickRandom(brand);
        return Task.FromResult(_random.PickRandom(release).Clone());
    }

    private static DeviceInfoApiDevice[][] GetEligibleReleases(
        IReadOnlyDictionary<string, DeviceInfoApiDevice[]> profiles, RandomDeviceSelection selection)
    {
        IEnumerable<DeviceInfoApiDevice[]> releases = selection.Release is null
            ? profiles.Values
            : profiles.TryGetValue(selection.Release, out var matchingRelease) ? [matchingRelease] : [];
        string sdk = selection.Sdk.ToString(CultureInfo.InvariantCulture);
        return releases.Select(records => selection.Sdk == 0
                ? records
                : records.Where(device => device.Sdk == sdk).ToArray())
            .Where(records => records.Length > 0).ToArray();
    }

    private CatalogSnapshot LoadCatalog()
    {
        var assembly = typeof(AssetDataReader).Assembly;
        string prefix = assembly.GetName().Name + "." + AssetConstants.Data.DeviceInfoPath.Replace('/', '.');
        string[] resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                && name.EndsWith("_device_info.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
            throw new DeviceRandomApiException("Embedded device info files were not found.");

        var profiles = new Dictionary<string, IReadOnlyDictionary<string, DeviceInfoApiDevice[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (string resource in resources)
        {
            string path = AssetConstants.Data.DeviceInfoPath + resource[prefix.Length..];
            var (brand, records) = LoadBrandProfiles(path);
            profiles.Add(brand, records);
        }

        var versions = profiles.ToDictionary(pair => pair.Key,
            pair => FormatVersions(pair.Value.Keys), StringComparer.OrdinalIgnoreCase);
        versions.Add("Random", FormatVersions(profiles.Values.SelectMany(releases => releases.Keys)));
        IReadOnlyList<string> brands = Array.AsReadOnly(profiles.Keys.Select(DisplayBrand)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray());
        _logger.LogInformation("Loaded {Records} device info records across {Brands} brands.",
            profiles.Values.Sum(releases => releases.Values.Sum(records => records.Length)), brands.Count);
        return new CatalogSnapshot(profiles, versions, brands);
    }

    private (string Brand, IReadOnlyDictionary<string, DeviceInfoApiDevice[]> Profiles) LoadBrandProfiles(string path)
    {
        using var document = JsonDocument.Parse(AssetDataReader.ReadText(path));
        JsonElement root = document.RootElement;
        string brand = Read(root, "brand") ?? throw new DeviceRandomApiException($"Missing brand in {path}.");
        JsonElement records = root.GetProperty("records");
        if (root.GetProperty("count").GetInt32() != records.GetArrayLength())
            throw new DeviceRandomApiException($"Device info count is incorrect in {path}.");

        var valid = new List<DeviceInfoApiDevice>();
        foreach (JsonElement record in records.EnumerateArray())
        {
            DeviceInfoApiDevice device = MapRecord(record);
            try
            {
                DeviceProfileNormalizer.NormalizeIdentity(device);
                if (!device.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase))
                    throw new DeviceRandomApiException("Record brand does not match its source file.");
                valid.Add(device);
            }
            catch (DeviceRandomApiException exception)
            {
                _logger.LogWarning("Skipping device info in {Path}, model {Model}, fingerprint {Fingerprint}: {Reason}",
                    path, device.Model, device.Fingerprint, exception.Message);
            }
        }

        if (valid.Count == 0)
            throw new DeviceRandomApiException($"No usable device info records in {path}.");
        return (brand, valid.GroupBy(device => device.Release)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal));
    }

    private static IReadOnlyList<string> FormatVersions(IEnumerable<string> releases)
        => Array.AsReadOnly(releases.Distinct(StringComparer.Ordinal)
            .OrderBy(release => Version.Parse(release.Contains('.') ? release : release + ".0"))
            .Select(release => "Android " + release).ToArray());

    private static string DisplayBrand(string brand) => brand.ToLowerInvariant() switch
    {
        "asus" => "ASUS", "poco" => "POCO", "oppo" => "OPPO", "oneplus" => "OnePlus",
        "realme" => "realme", "vivo" => "vivo",
        _ => char.ToUpperInvariant(brand[0]) + brand[1..]
    };

    private static bool IsRandom(string? value) => string.IsNullOrWhiteSpace(value)
        || value.Trim().Equals("Random", StringComparison.OrdinalIgnoreCase);

    private static string? Read(JsonElement record, string name)
        => record.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    private static DeviceInfoApiDevice MapRecord(JsonElement record) => new()
    {
        Brand = Read(record, "brand") ?? string.Empty,
        Manufacturer = Read(record, "manufacturer"), Model = Read(record, "model"),
        Product = Read(record, "product") ?? string.Empty, Name = Read(record, "name"),
        Code = Read(record, "device") ?? string.Empty, Board = Read(record, "board"),
        Fingerprint = Read(record, "fingerprint"), Hardware = Read(record, "hardware"),
        Platform = Read(record, "platform"),
        SocManufacturer = Read(record, "soc_manufacturer") ?? string.Empty,
        SocModel = Read(record, "soc_model") ?? string.Empty,
        BuildId = Read(record, "build_id") ?? string.Empty,
        BuildDisplayId = Read(record, "build_display_id"),
        BuildDate = Read(record, "build_date"), BuildDateUtc = Read(record, "build_date_utc"),
        BuildIncremental = Read(record, "build_incremental") ?? string.Empty,
        BuildHost = Read(record, "build_host"), BuildUser = Read(record, "build_user") ?? string.Empty,
        BuildFlavor = Read(record, "build_flavor") ?? string.Empty,
        BuildDescription = Read(record, "build_description") ?? string.Empty,
        Bootloader = Read(record, "bootloader"), Baseband = Read(record, "gms_baseband"),
        Type = Read(record, "type") ?? string.Empty, Tags = Read(record, "tags") ?? string.Empty,
        SecurityPatch = Read(record, "security_patch"),
        Release = Read(record, "release") ?? string.Empty, Sdk = Read(record, "sdk") ?? string.Empty
    };

    private sealed record CatalogSnapshot(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, DeviceInfoApiDevice[]>> Profiles,
        IReadOnlyDictionary<string, IReadOnlyList<string>> AndroidVersions,
        IReadOnlyList<string> Brands);
}
