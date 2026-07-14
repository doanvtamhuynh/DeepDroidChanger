using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

public static class DeviceInfoApiOptionsHelper
{
    public static void ApplyDefaults(DeviceInfoApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Endpoint = DeviceInfoApiConstants.Endpoint;
        options.UserPoolId = DeviceInfoApiConstants.UserPoolId;
        options.ClientId = DeviceInfoApiConstants.ClientId;
        options.Region = DeviceInfoApiConstants.Region;
        options.AuthenticationHeaderName = DeviceInfoApiConstants.AuthenticationHeaderName;
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
