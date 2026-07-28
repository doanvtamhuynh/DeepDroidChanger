using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

public static class DeviceInfoApiOptionsHelper
{
    public static void ApplyDefaults(DeviceInfoApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Endpoint = UrlConstants.DeviceInfoApi;
        options.UserPoolId = AuthenticationConstants.UserPoolId;
        options.ClientId = AuthenticationConstants.ClientId;
        options.Region = AuthenticationConstants.Region;
        options.AuthenticationHeaderName = AuthenticationConstants.HeaderName;
    }

    public static bool IsValid(DeviceInfoApiOptions options)
    {
        return options != null
            && Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _)
            && !string.IsNullOrWhiteSpace(options.UserPoolId)
            && !string.IsNullOrWhiteSpace(options.ClientId)
            && !string.IsNullOrWhiteSpace(options.Region)
            && !string.IsNullOrWhiteSpace(options.AuthenticationHeaderName);
    }
}
