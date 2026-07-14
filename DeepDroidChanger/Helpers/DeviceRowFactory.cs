using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Helpers;

public static class DeviceRowFactory
{
    public static void MergeSelectedDevices(
        List<StoredDeviceConfig> storedDevices,
        IEnumerable<StoredDeviceConfig> selectedDevices)
    {
        foreach (StoredDeviceConfig selectedDevice in selectedDevices)
        {
            if (ContainsSerial(storedDevices, selectedDevice.Serial))
                continue;

            storedDevices.Add(new StoredDeviceConfig
            {
                Serial = selectedDevice.Serial,
                Name = selectedDevice.Name,
                Type = selectedDevice.Type
            });
        }
    }

    public static DeviceRowViewModel CreateDeviceRow(
        int index,
        StoredDeviceConfig storedDevice,
        AdbDevice? connectedDevice,
        string statusText,
        string readyLogText)
    {
        return new DeviceRowViewModel(
            index,
            false,
            storedDevice.Serial,
            storedDevice.Name,
            storedDevice.Type,
            DeviceFilterKeys.Inactive,
            connectedDevice?.Status ?? AdbDeviceStatus.Offline,
            statusText,
            readyLogText);
    }

    public static bool ContainsSerial(IEnumerable<StoredDeviceConfig> devices, string serial)
    {
        return devices.Any(device => SerialEquals(device.Serial, serial));
    }

    public static bool SerialEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
