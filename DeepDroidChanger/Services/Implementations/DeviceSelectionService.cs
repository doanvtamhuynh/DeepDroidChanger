namespace DeepDroidChanger.Services;

public sealed class DeviceSelectionService : IDeviceSelectionService
{
    public string? FindSelectionSerial(
        string? targetSerial,
        IReadOnlyList<string> visibleSerials,
        IReadOnlyList<string> allSerials)
    {
        if (string.IsNullOrWhiteSpace(targetSerial))
            return visibleSerials.FirstOrDefault();

        return visibleSerials.FirstOrDefault(serial => SerialEquals(serial, targetSerial))
            ?? allSerials.FirstOrDefault(serial => SerialEquals(serial, targetSerial))
            ?? visibleSerials.FirstOrDefault();
    }

    private static bool SerialEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
