using System.Text.Json;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

public static class IpGeolocationResponseParser
{
    public static bool TryParse(string json, out IpGeolocationInfo info)
    {
        info = new IpGeolocationInfo();
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            bool success = root.TryGetProperty("success", out JsonElement successElement)
                && successElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                && successElement.GetBoolean();
            string timezone = GetNestedString(
                root,
                "timezone",
                "id");

            info = new IpGeolocationInfo
            {
                Success = success,
                PublicIp = GetString(root, "ip"),
                CountryCode = GetString(root, "country_code"),
                Latitude = GetDouble(root, "latitude"),
                Longitude = GetDouble(root, "longitude"),
                Timezone = timezone,
                Message = GetString(root, "message")
            };

            return info.Success && !string.IsNullOrWhiteSpace(info.Timezone);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()?.Trim() ?? string.Empty
                : string.Empty;
    }

    private static string GetNestedString(JsonElement element, string objectName, string propertyName)
    {
        return element.TryGetProperty(objectName, out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Object
                ? GetString(nested, propertyName)
                : string.Empty;
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.TryGetDouble(out double value)
                ? value
                : 0;
    }
}
