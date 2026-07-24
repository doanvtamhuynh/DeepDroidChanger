using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

public static class DeviceChangeOptionsHelper
{
    public static DeviceChangeOptions CreateNormalizedCopy(
        DeviceChangeOptions? source,
        bool? useDefaultMode = null)
    {
        source ??= new DeviceChangeOptions();
        return new DeviceChangeOptions
        {
            UseDefaultMode = useDefaultMode ?? source.UseDefaultMode,
            ChangeAndroidId = source.ChangeAndroidId,
            ChangeMacAddress = source.ChangeMacAddress,
            UpdateIntegrity = source.UpdateIntegrity,
            ChangeTimezone = source.ChangeTimezone,
            ChangeLocation = source.ChangeLocation,
            UseRmRfForPackageCleanup = source.UseRmRfForPackageCleanup,
            ClearAllPackages = source.ClearAllPackages,
            ClearSelectedPackages = source.ClearSelectedPackages,
            ClearGooglePackages = source.ClearGooglePackages,
            ClearGoogleAccounts = source.ClearGoogleAccounts,
            SelectedPackages = NormalizePackageNames(source.SelectedPackages)
        };
    }

    public static List<string> NormalizePackageNames(IEnumerable<string>? packageNames)
    {
        return (packageNames ?? [])
            .Where(packageName => !string.IsNullOrWhiteSpace(packageName))
            .Select(packageName => packageName.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(packageName => packageName, StringComparer.Ordinal)
            .ToList();
    }

    public static bool HasPackageCleanup(DeviceChangeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseDefaultMode
            || options.ClearAllPackages
            || options.ClearSelectedPackages
            || options.ClearGooglePackages
            || options.ClearGoogleAccounts;
    }
}
