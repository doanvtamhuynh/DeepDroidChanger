using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

public static class DeviceInfoApiOptionsHelper
{
    public static void ApplyDefaults(DeviceInfoApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Endpoint = UrlConstants.DeviceInfoGraphQlApi;
        options.AuthorizationHeaderName = "authorization";
    }

    public static bool IsValid(DeviceInfoApiOptions options)
    {
        return options != null
            && Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _)
            && !string.IsNullOrWhiteSpace(options.AuthorizationHeaderName);
    }
}
