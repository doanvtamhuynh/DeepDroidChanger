namespace DeepDroidChanger.Constants;

public static class DeviceProfileOptions
{
    public const string Random = "Random";
    public const string Android13 = "Android 13";
    public const string Android14 = "Android 14";
    public const string Android15 = "Android 15";

    public static IReadOnlyList<string> Brands { get; } =
        [Random, "Google", "Samsung", "Xiaomi", "OnePlus", "OPPO", "vivo"];

    public static IReadOnlyList<string> SupportedAndroidVersions { get; } =
        [Android13, Android14, Android15];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> AndroidVersionsByBrand { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = SupportedAndroidVersions,
            ["Samsung"] = SupportedAndroidVersions,
            ["Xiaomi"] = SupportedAndroidVersions,
            ["OnePlus"] = [Android13],
            ["OPPO"] = [Android14],
            ["vivo"] = [Android14]
        };
}
