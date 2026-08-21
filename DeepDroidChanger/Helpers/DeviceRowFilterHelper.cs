using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Helpers;

public static class DeviceRowFilterHelper
{
    public static bool Matches(
        DeviceRowViewModel device,
        string selectedFilter,
        string searchText)
    {
        ArgumentNullException.ThrowIfNull(device);

        bool matchesFilter = selectedFilter switch
        {
            "Online" => device.ConnectionStatus == AdbDeviceStatus.Online,
            "Offline" => device.ConnectionStatus != AdbDeviceStatus.Online,
            "Active" => string.Equals(device.Active, "Active", StringComparison.OrdinalIgnoreCase),
            "Inactive" => string.Equals(device.Active, "Inactive", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        if (!matchesFilter)
            return false;

        string search = searchText.Trim();
        return search.Length == 0
            || device.Serial.Contains(search, StringComparison.OrdinalIgnoreCase)
            || device.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || device.Type.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
