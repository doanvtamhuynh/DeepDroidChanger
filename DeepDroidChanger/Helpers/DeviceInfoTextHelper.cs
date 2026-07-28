using DeepDroidChanger.Services;

namespace DeepDroidChanger.Helpers;

public static class DeviceInfoTextHelper
{
    public static string Create(
        ILocalizationService localizationService,
        string deviceName,
        string deviceSerial)
    {
        string name = string.IsNullOrWhiteSpace(deviceName) ? deviceSerial : deviceName;
        string format = localizationService.GetString("Log_DeviceInfoTextFormat");
        return string.Format(format, name, deviceSerial);
    }
}
