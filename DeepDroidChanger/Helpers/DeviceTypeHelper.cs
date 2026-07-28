
namespace DeepDroidChanger.Helpers;

public static class DeviceTypeHelper
{
    public static string Normalize(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "sargo" => "sargo",
            "starlte" => "starlte",
            "tissot" => "tissot",
            _ => "unknown"
        };
    }

    public static string GetDefaultName(string? type)
    {
        return Normalize(type) switch
        {
            "sargo" => "3A",
            "starlte" => "S9",
            "tissot" => "MiA1",
            _ => "unknown"
        };
    }
}
