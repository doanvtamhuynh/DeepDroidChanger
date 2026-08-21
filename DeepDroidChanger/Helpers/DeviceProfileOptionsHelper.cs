using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

public static class DeviceProfileOptionsHelper
{
    public static IReadOnlyList<string> Brands { get; } =
        ["Random", "Google", "Samsung", "Xiaomi", "OnePlus", "OPPO", "vivo"];

    public static IReadOnlyList<string> GetAndroidVersions(string? brand)
    {
        IReadOnlyList<string> compatibleVersions = brand?.Trim().ToLowerInvariant() switch
        {
            "oneplus" => ["Android 13"],
            "oppo" or "vivo" => ["Android 14"],
            _ => ["Android 13", "Android 14", "Android 15"]
        };

        return ["Random", .. compatibleVersions];
    }

    public static string? FindOption(IEnumerable<string> options, string? value)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalizedValue = value.Trim();
        return options.FirstOrDefault(option =>
            string.Equals(option, normalizedValue, StringComparison.OrdinalIgnoreCase));
    }

    public static CarrierCountryOption? FindCountryByIso(
        IEnumerable<CarrierCountryOption> countries,
        string? countryIso)
    {
        ArgumentNullException.ThrowIfNull(countries);
        if (string.IsNullOrWhiteSpace(countryIso))
            return null;

        string normalizedIso = countryIso.Trim();
        return countries.FirstOrDefault(country =>
            string.Equals(
                country.CountryIso,
                normalizedIso,
                StringComparison.OrdinalIgnoreCase));
    }

    public static CarrierCountryOption? FindCountryByName(
        IEnumerable<CarrierCountryOption> countries,
        string? countryName)
    {
        ArgumentNullException.ThrowIfNull(countries);
        if (string.IsNullOrWhiteSpace(countryName))
            return null;

        string normalizedName = countryName.Trim();
        return countries.FirstOrDefault(country =>
            string.Equals(
                country.CountryName,
                normalizedName,
                StringComparison.OrdinalIgnoreCase));
    }
}
