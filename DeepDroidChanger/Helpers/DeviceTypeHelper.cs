using DeepDroidChanger.Constants;

namespace DeepDroidChanger.Helpers;

public static class DeviceTypeHelper
{
    public static string Normalize(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            DeviceTypeOptions.Sargo => DeviceTypeOptions.Sargo,
            DeviceTypeOptions.Starlte => DeviceTypeOptions.Starlte,
            DeviceTypeOptions.Tissot => DeviceTypeOptions.Tissot,
            _ => DeviceTypeOptions.Unknown
        };
    }

    public static string GetDefaultName(string? type)
    {
        return Normalize(type) switch
        {
            DeviceTypeOptions.Sargo => "3A",
            DeviceTypeOptions.Starlte => "S9",
            DeviceTypeOptions.Tissot => "MiA1",
            _ => DeviceTypeOptions.Unknown
        };
    }
}
