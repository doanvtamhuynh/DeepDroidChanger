using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.Helpers;

public static class DeviceProfileOptionsHelper
{
    public static IReadOnlyList<string> GetBrands(IDeviceInfoCatalogService? catalog)
        => ["Random", .. catalog?.Brands ?? []];

    public static IReadOnlyList<string> GetAndroidVersions(string? brand, IDeviceInfoCatalogService? catalog)
        => ["Random", .. catalog?.GetAndroidVersions(brand) ?? []];

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
