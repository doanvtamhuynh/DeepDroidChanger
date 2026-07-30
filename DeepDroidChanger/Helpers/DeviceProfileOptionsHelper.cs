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
}
